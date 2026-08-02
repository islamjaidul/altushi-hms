using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Hms.Kernel.Hosting;

/// <summary>
/// The outermost catch (spec 0039 WP6, AUD-VAL-01/07): no operator ever sees a blank 500 or a
/// stack trace. A domain rule violation renders its own sentence; a database refusal (CHECK,
/// unique, FK, deadlock, pool exhaustion) is translated to one; anything else gets a recoverable
/// error page and a full log entry.
/// <para>
/// This must ship in the same change as the WP2 domain CHECK constraints: SQLSTATE
/// <c>23514</c> was previously caught nowhere, so every constraint the schema gained would have
/// surfaced as a crash instead of a refusal — the audit's single most important sequencing
/// warning.
/// </para>
/// </summary>
public sealed class FaultBoundaryMiddleware(RequestDelegate next, ILogger<FaultBoundaryMiddleware> log)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, message) = Translate(ex);
            if (status >= 500)
                log.LogError(ex, "Unhandled fault on {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            else
                log.LogWarning("Refused {Method} {Path}: {Message}",
                    context.Request.Method, context.Request.Path, message);

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(Render(message));
        }
    }

    private static (int Status, string Message) Translate(Exception ex)
    {
        // EF wraps the database's refusal; unwrap to the PostgresException underneath.
        var pg = ex as PostgresException
                 ?? (ex as DbUpdateException)?.InnerException as PostgresException;
        if (pg is not null)
            return pg.SqlState switch
            {
                PostgresErrorCodes.CheckViolation => (StatusCodes.Status400BadRequest,
                    CheckMessage(pg.ConstraintName)),
                PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict,
                    "That was already saved. Reload the screen to see the current state before trying again."),
                PostgresErrorCodes.ForeignKeyViolation => (StatusCodes.Status400BadRequest,
                    "This refers to a record that does not exist any more. Reload the screen and pick again."),
                PostgresErrorCodes.NotNullViolation => (StatusCodes.Status400BadRequest,
                    "A required value was missing, so nothing was saved. Fill in every required box and try again."),
                PostgresErrorCodes.DeadlockDetected => (StatusCodes.Status409Conflict,
                    "Two people saved at the same moment and this attempt was set aside — nothing was lost. Try again."),
                PostgresErrorCodes.TooManyConnections or PostgresErrorCodes.CannotConnectNow =>
                    (StatusCodes.Status503ServiceUnavailable,
                    "The system is unusually busy right now. Wait a moment and try again — nothing was saved."),
                PostgresErrorCodes.SerializationFailure => (StatusCodes.Status409Conflict,
                    "Someone else changed this record at the same moment. Try again."),
                _ => (StatusCodes.Status500InternalServerError, RecoverableSentence),
            };

        if (ex is DbUpdateConcurrencyException)
            return (StatusCodes.Status409Conflict,
                "Someone else changed this record while you were working. Reload the screen to see "
                + "the latest, then make your change again.");

        // A module's own rule (BillingException, IpdException, …): sealed per-module types whose
        // message is written for the operator. Recognised by convention — an Hms.* exception
        // deriving directly from Exception is a domain refusal, not a fault.
        if (ex.GetType() is { BaseType: { } b } t && b == typeof(Exception)
            && t.Namespace?.StartsWith("Hms.", StringComparison.Ordinal) == true
            && t.Name.EndsWith("Exception", StringComparison.Ordinal))
            return (StatusCodes.Status400BadRequest, ex.Message);

        return (StatusCodes.Status500InternalServerError, RecoverableSentence);
    }

    private const string RecoverableSentence =
        "Something went wrong on our side and this action was not saved. Go back and try again — "
        + "if it happens twice, note the time and tell your administrator.";

    /// <summary>A slightly more specific sentence for the constraint families operators actually
    /// hit. The fallback is still a sentence, never a code.</summary>
    private static string CheckMessage(string? constraint) => constraint switch
    {
        not null when constraint.Contains("vitals") || constraint.Contains("glucose") =>
            "That reading is outside the range the chart accepts, so it was not saved. "
            + "Re-check the measurement and enter it again.",
        not null when constraint.Contains("qty") =>
            "That quantity is outside the range this screen accepts, so nothing was saved. "
            + "Check the number and try again.",
        not null when constraint.Contains("state") =>
            "That change is not one this record can take from its current state. "
            + "Reload the screen to see where it is now.",
        not null when constraint.Contains("tender") =>
            "That is not a way money can be taken at this counter. Choose one of the listed tenders.",
        not null when constraint.Contains("pct") || constraint.Contains("percent") =>
            "A percentage must be between 0 and 100. Nothing was saved.",
        not null when constraint.Contains("dob") || constraint.Contains("date") ||
                      constraint.Contains("valid_from") =>
            "That date is not one this screen can accept. Check it and try again.",
        _ => "A value on this screen is outside the range the system accepts, so nothing was "
             + "saved. Check the numbers and dates and try again.",
    };

    /// <summary>Standalone markup: no layout dependency, no framework type names, and the same
    /// <c>alert bad</c> shape every screen uses, so the sentence reads like the product.</summary>
    private static string Render(string message)
    {
        var safe = System.Net.WebUtility.HtmlEncode(message);
        return $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><title>Not saved</title>
            <link rel="stylesheet" href="/_content/Hms.Shell/css/tokens.css">
            <link rel="stylesheet" href="/_content/Hms.Shell/css/app.css">
            </head>
            <body>
            <main class="content" style="max-width:640px;margin:10vh auto;padding:0 16px">
                <div class="alert bad">
                    <span class="icon icon-sm" aria-hidden="true">error</span>
                    <span>{{safe}}</span>
                </div>
                <p style="margin-top:16px">
                    <a href="javascript:history.back()">&#8592; Go back to the screen you were on</a>
                </p>
            </main>
            </body>
            </html>
            """;
    }
}
