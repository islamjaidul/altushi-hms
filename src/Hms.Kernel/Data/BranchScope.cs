using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Hms.Kernel.Data;

/// <summary>
/// The branch of the request in flight (spec 0039 WP5, AUD-ARCH-01). Set once per request by
/// <c>BranchResolutionMiddleware</c> from the signed-in user's <c>branch_id</c> claim; flows
/// through async work; defaults to branch 1 for startup seeding and anonymous surfaces —
/// exactly the constant the whole product used to hardcode.
/// </summary>
public static class BranchScope
{
    public const string ClaimType = "branch_id";
    private static readonly AsyncLocal<long> Value = new();

    public static long Current
    {
        get => Value.Value == 0 ? 1 : Value.Value;
        set => Value.Value = value;
    }
}

public static class BranchIsolation
{
    /// <summary>
    /// Branch isolation as structure, not memory (WP5): every entity that carries a
    /// <c>BranchId</c> gets a global query filter tied to the context instance's
    /// <c>CurrentBranch</c>, so a query cannot forget its branch predicate — it would have to
    /// call <c>IgnoreQueryFilters()</c> out loud. The architecture test asserts full coverage.
    /// </summary>
    /// <remarks>The context must expose <c>public long CurrentBranch</c>; referencing it
    /// through the context instance is what lets EF re-target the filter to each live context,
    /// per the documented multi-tenant pattern.</remarks>
    public static void ApplyBranchIsolation(this ModelBuilder b, DbContext ctx)
    {
        var branchProp = ctx.GetType().GetProperty("CurrentBranch")
            ?? throw new InvalidOperationException(
                $"{ctx.GetType().Name} needs a public long CurrentBranch property for branch isolation.");

        foreach (var entity in b.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue;
            var prop = entity.FindProperty("BranchId");
            if (prop is null || prop.ClrType != typeof(long)) continue;

            var e = Expression.Parameter(entity.ClrType, "e");
            var body = Expression.Equal(
                Expression.Call(typeof(EF), nameof(EF.Property), [typeof(long)], e,
                    Expression.Constant("BranchId")),
                Expression.Property(Expression.Constant(ctx), branchProp));
            entity.SetQueryFilter(Expression.Lambda(body, e));
        }
    }
}
