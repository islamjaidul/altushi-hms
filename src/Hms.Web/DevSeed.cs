using Hms.Admin.Data;
using Hms.Appointments.Data;
using Hms.Billing.Data;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// Development/demo seed: branch, §12 role templates with module.action permissions, and the
/// demo cast (07 §1). Idempotent; runs only when Seed:DevUsers=true (never in production).
/// The full 90-day history generator is the S6 deliverable — this is just enough for login+nav.
/// </summary>
public static class DevSeed
{
    private static readonly Dictionary<string, string[]> Roles = new()
    {
        ["Receptionist"] =
            ["registration.create", "registration.read", "appointments.read", "appointments.create"],
        ["Billing Operator"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.open", "billing.session.close", "diagnostics.order.create"],
        // §12 gives both lab roles "Patient reg R" and "Diag order R"; the Pathologist's LIS
        // cell is "A(verify) + C", so verification and entry both belong to that role.
        ["Lab Technologist"] =
            ["registration.read", "lis.worklist.read", "lis.sample.collect", "lis.result.enter"],
        ["Pathologist"] =
            ["registration.read", "lis.worklist.read", "lis.result.enter", "lis.result.verify"],
        ["Billing Supervisor"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.close", "admin.approvals.decide"],
        ["Admin"] =
            ["admin.users.manage", "admin.audit.read", "admin.approvals.decide",
             "admin.masters.manage", "notifications.read"],
        ["MD"] =
            ["dashboard.read", "admin.approvals.decide", "admin.audit.read"],
    };

    private static readonly (string User, string Display, string Role)[] Cast =
    [
        ("jashim", "Jashim Uddin", "Receptionist"),
        ("rasel", "Rasel Ahmed", "Billing Operator"),
        ("ripon", "Ripon Das", "Lab Technologist"),
        ("farhana", "Dr. Farhana Rahman", "Pathologist"),
        ("shahid", "Shahid Alam", "Billing Supervisor"),
        ("admin", "System Admin", "Admin"),
        ("md", "Dr. Chairman", "MD"),
    ];

    public const string DevPassword = "Demo#1234";   // on the demo card (07 §1)

    public static async Task RunAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Seed:DevUsers", false)) return;

        var kdb = sp.GetRequiredService<KernelDbContext>();
        if (!await kdb.Branches.AnyAsync())
        {
            kdb.Branches.Add(new Branch { Code = "MAIN", Name = "Altushi General Hospital" });
            await kdb.SaveChangesAsync();
        }

        var roleMgr = sp.GetRequiredService<RoleManager<AppRole>>();
        var userMgr = sp.GetRequiredService<UserManager<AppUser>>();
        var adb = sp.GetRequiredService<AuthDbContext>();

        foreach (var (roleName, perms) in Roles)
        {
            var role = await roleMgr.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new AppRole { Name = roleName, System = true };
                (await roleMgr.CreateAsync(role)).ThrowIfFailed();
            }
            foreach (var p in perms)
            {
                var parts = p.Split('.', 2);   // "billing.invoice.create" → module "billing", action "invoice.create"
                if (!await adb.Permissions.AnyAsync(x =>
                        x.RoleId == role.Id && x.Module == parts[0] && x.Action == parts[1]))
                    adb.Permissions.Add(new Permission { RoleId = role.Id, Module = parts[0], Action = parts[1] });
            }
        }
        await adb.SaveChangesAsync();

        foreach (var (userName, display, roleName) in Cast)
        {
            if (await userMgr.FindByNameAsync(userName) is not null) continue;
            var user = new AppUser { UserName = userName, DisplayName = display };
            (await userMgr.CreateAsync(user, DevPassword)).ThrowIfFailed();
            (await userMgr.AddToRoleAsync(user, roleName)).ThrowIfFailed();
        }

        await SeedOperationalAsync(sp, kdb);
    }

    /// <summary>Counters, starter catalog with rates, and §12 approval policies — enough for the
    /// golden thread on a fresh install. The full 90-day history generator remains spec 0010.</summary>
    private static async Task SeedOperationalAsync(IServiceProvider sp, KernelDbContext kdb)
    {
        var bill = sp.GetRequiredService<BillDbContext>();
        if (!await bill.Counters.AnyAsync())
        {
            bill.Counters.AddRange(
                new Counter { BranchId = 1, Name = "Front Desk 1", Kind = "front-desk" },
                new Counter { BranchId = 1, Name = "Diagnostics Counter", Kind = "diagnostics" },
                new Counter { BranchId = 1, Name = "Emergency Counter", Kind = "er" });
            await bill.SaveChangesAsync();
        }

        if (!await kdb.ApprovalPolicies.AnyAsync())
        {
            kdb.ApprovalPolicies.AddRange(
                new ApprovalPolicy { Type = "discount", Tier = 0, Role = "Billing Operator", ThresholdMin = 200 },
                new ApprovalPolicy { Type = "discount", Tier = 1, Role = "Billing Supervisor", EscalationMinutes = 10 },
                new ApprovalPolicy { Type = "discount", Tier = 2, Role = "MD" },
                new ApprovalPolicy { Type = "refund", Tier = 1, Role = "Billing Supervisor", EscalationMinutes = 10 },
                new ApprovalPolicy { Type = "refund", Tier = 2, Role = "MD" },
                new ApprovalPolicy { Type = "reopen", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "carry_close", Tier = 1, Role = "Billing Supervisor" });
            await kdb.SaveChangesAsync();
        }

        // Doctors (07 §1 cast). §9A.2 keeps appointments "lite": the queue is by date, so one
        // schedule row per doctor carries the room and capacity the serial screen needs.
        var appt = sp.GetRequiredService<ApptDbContext>();
        if (!await appt.Schedules.AnyAsync())
        {
            var doctors = new (long id, string name, string room, int max)[]
            {
                (1, "Dr. Kamrul Hasan", "Room 204 — Medicine", 40),
                (2, "Dr. Nusrat Jahan", "Room 106 — Gynae & Obs", 30),
                (3, "Dr. Sohel Rana", "Room 301 — Cardiology", 25),
                (4, "Dr. Ashraf Ali", "Room 112 — Paediatrics", 35),
            };
            foreach (var (id, name, room, max) in doctors)
                appt.Schedules.Add(new DoctorSchedule
                {
                    DoctorId = id, DoctorName = name, Room = room, MaxSerials = max,
                    Weekday = 0, SlotFrom = new TimeOnly(9, 0), SlotTo = new TimeOnly(14, 0),
                });
            await appt.SaveChangesAsync();
        }

        var adm = sp.GetRequiredService<AdmDbContext>();

        // §5 M8 [M] referrer capture, and 5A-R1 reporting consultants (07 §1 cast).
        if (!await adm.Referrers.AnyAsync())
        {
            adm.Referrers.AddRange(
                new Referrer { Code = "SELF", Name = "Self / walk-in", Kind = "self", CommissionPercent = 0 },
                new Referrer { Code = "RD-041", Name = "Dr. S. Chowdhury", Kind = "doctor", Area = "Zindabazar", Phone = "01711-000041", CommissionPercent = 15 },
                new Referrer { Code = "RD-042", Name = "Popular Pharmacy", Kind = "agent", Area = "Amberkhana", Phone = "01711-000042", CommissionPercent = 10 },
                new Referrer { Code = "RD-043", Name = "Dr. M. Ali", Kind = "doctor", Area = "Subid Bazar", Phone = "01711-000043", CommissionPercent = 15 },
                new Referrer { Code = "CORP-01", Name = "Rose Garments Ltd.", Kind = "corporate", Area = "Sylhet Sadar", CommissionPercent = 0 });
            await adm.SaveChangesAsync();
        }

        if (!await adm.ReportingConsultants.AnyAsync())
        {
            adm.ReportingConsultants.AddRange(
                new ReportingConsultant { Name = "Dr. Farhana Rahman", Degrees = "MBBS, MD (Pathology)", BmdcNo = "A-38112", Departments = ["Hematology", "Biochemistry", "Pathology", "Immunology"] },
                new ReportingConsultant { Name = "Dr. N. Chowdhury", Degrees = "MBBS, MD (Radiology)", BmdcNo = "A-45120", Departments = ["Imaging"] },
                new ReportingConsultant { Name = "Dr. A. Karim", Degrees = "MBBS, D-Card", BmdcNo = "A-51907", Departments = ["Cardiology"] });
            await adm.SaveChangesAsync();
        }

        if (!await adm.Services.AnyAsync())
        {
            var from = new DateOnly(2026, 1, 1);
            var services = new (string code, string name, string dept, long price)[]
            {
                ("CON-GEN", "Doctor Consultation (General)", "OPD", 700),
                ("CON-SPC", "Doctor Consultation (Specialist)", "OPD", 1200),
                ("ER-ATT", "Emergency Attendance", "Emergency", 500),
                ("DRS-MIN", "Dressing (Minor)", "Emergency", 300),
            };
            foreach (var (code, name, dept, price) in services)
            {
                var svc = new Service { Code = code, Name = name, Dept = dept, Kind = "consult" };
                adm.Services.Add(svc);
                await adm.SaveChangesAsync();
                adm.RateVersions.Add(new RateVersion
                {
                    BranchId = 1, CatalogKind = "service", CatalogId = svc.Id,
                    Price = price, ValidFrom = from, AuthorId = 1,
                });
            }
            var tests = new (string code, string name, string dept, string sample, int tat, long price)[]
            {
                ("CBC", "Complete Blood Count", "Hematology", "EDTA", 240, 400),
                ("ESR", "ESR", "Hematology", "EDTA", 240, 200),
                ("RBS", "Random Blood Sugar", "Biochemistry", "Serum", 60, 150),
                ("LIPID", "Lipid Profile", "Biochemistry", "Serum", 1440, 1200),
                ("SCR", "Serum Creatinine", "Biochemistry", "Serum", 240, 400),
                ("TSH", "TSH", "Immunology", "Serum", 1440, 900),
                ("URINE-RE", "Urine R/E", "Pathology", "Urine", 180, 250),
                ("XRAY-CH", "X-ray Chest P/A", "Imaging", "None", 120, 500),
                ("USG-ABD", "USG Whole Abdomen", "Imaging", "None", 240, 1500),
                ("ECG", "ECG 12-Lead", "Cardiology", "None", 60, 400),
            };
            foreach (var (code, name, dept, sample, tat, price) in tests)
            {
                var t = new TestCatalogItem
                {
                    Code = code, Name = name, Dept = dept,
                    SampleTypes = sample == "None" ? [] : [sample], TatMinutes = tat,
                    Template = ResultTemplates.For(code),
                };
                adm.TestCatalog.Add(t);
                await adm.SaveChangesAsync();
                adm.RateVersions.Add(new RateVersion
                {
                    BranchId = 1, CatalogKind = "test", CatalogId = t.Id,
                    Price = price, ValidFrom = from, AuthorId = 1,
                });
            }
            await adm.SaveChangesAsync();
        }

        // Backfill for catalogs seeded before result templates existed. Without this a database
        // created by an earlier build keeps `template = null`, and result entry silently renders
        // a test with no parameter rows — a blank screen with no error to explain it.
        var untemplated = await adm.TestCatalog.Where(t => t.Template == null).ToListAsync();
        var backfilled = 0;
        foreach (var t in untemplated)
        {
            var template = ResultTemplates.For(t.Code);
            if (template is null) continue;          // imaging/cardiology report a narrative
            t.Template = template;
            backfilled++;
        }
        if (backfilled > 0) await adm.SaveChangesAsync();
    }

    private static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
