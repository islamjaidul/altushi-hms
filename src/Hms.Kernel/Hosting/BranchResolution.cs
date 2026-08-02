using Hms.Kernel.Data;
using Microsoft.AspNetCore.Http;

namespace Hms.Kernel.Hosting;

/// <summary>
/// Stamps the request's ambient <see cref="BranchScope"/> from the signed-in user's
/// <c>branch_id</c> claim (spec 0039 WP5, AUD-ARCH-01). Every DbContext constructed during the
/// request captures it, so branch isolation holds without any query remembering to filter.
/// Anonymous surfaces and startup work run as branch 1 — the historical constant.
/// </summary>
public sealed class BranchResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var claim = context.User.FindFirst(BranchScope.ClaimType)?.Value;
        BranchScope.Current = long.TryParse(claim, out var b) && b > 0 ? b : 1;
        await next(context);
    }
}
