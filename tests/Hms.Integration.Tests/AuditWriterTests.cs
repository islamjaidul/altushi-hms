using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

// ADR-0011 / G6: every financial write produces its audit event in the same transaction.
[Collection("postgres")]
public class AuditWriterTests(PostgresFixture pg)
{
    private readonly AuditWriter _audit = new(TimeProvider.System);

    [Fact]
    public async Task Change_and_audit_commit_together()
    {
        await using var db = pg.CreateKernelContext();
        await using var tx = await db.Database.BeginTransactionAsync();

        var branch = new Branch { Code = "AUD1", Name = "Audit Test Branch" };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        _audit.Append(db, branch.Id, actorId: 7, "Test Operator", "branch.create",
            "kernel.branch", branch.Id, after: new { branch.Code });
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        await using var check = pg.CreateKernelContext();
        Assert.NotNull(await check.Branches.SingleOrDefaultAsync(b => b.Code == "AUD1"));
        var evt = await check.AuditEvents.SingleAsync(e => e.Entity == "kernel.branch" && e.EntityId == branch.Id);
        Assert.Equal("branch.create", evt.Action);
        Assert.Equal("Test Operator", evt.ActorNameSnapshot);
    }

    [Fact]
    public async Task Rollback_removes_both_change_and_audit()
    {
        long branchId;
        await using (var db = pg.CreateKernelContext())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var branch = new Branch { Code = "AUD2", Name = "Rolled Back" };
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            branchId = branch.Id;
            _audit.Append(db, branch.Id, 7, "Test Operator", "branch.create", "kernel.branch", branch.Id);
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }

        await using var check = pg.CreateKernelContext();
        Assert.Null(await check.Branches.SingleOrDefaultAsync(b => b.Code == "AUD2"));
        Assert.False(await check.AuditEvents.AnyAsync(e => e.EntityId == branchId && e.Entity == "kernel.branch"));
    }
}
