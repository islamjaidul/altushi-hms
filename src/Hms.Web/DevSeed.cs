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
        // §12 front-desk row + US6.3: the front desk manages beds, admissions and transfers.
        ["Receptionist"] =
            ["registration.create", "registration.read", "appointments.read", "appointments.create",
             "ipd.read", "ipd.manage"],
        // US6.2: the billing operator settles discharges, takes advances, issues certificates.
        ["Billing Operator"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.open", "billing.session.close", "diagnostics.order.create",
             "ipd.read", "ipd.settle"],
        // §12 gives both lab roles "Patient reg R" and "Diag order R"; the Pathologist's LIS
        // cell is "A(verify) + C", so verification and entry both belong to that role.
        ["Lab Technologist"] =
            ["registration.read", "lis.worklist.read", "lis.sample.collect", "lis.result.enter"],
        // Spec 0026: a reporting consultant signs lab and imaging alike — one verifier, one
        // e-sign path, two surfaces.
        ["Pathologist"] =
            ["registration.read", "lis.worklist.read", "lis.result.enter", "lis.result.verify",
             "radiology.worklist.read", "radiology.report.write"],
        ["Radiology Technician"] =
            ["registration.read", "radiology.worklist.read", "radiology.study.perform"],
        ["Billing Supervisor"] =
            ["registration.read", "billing.invoice.create", "billing.receipt.create",
             "billing.session.close", "admin.approvals.decide", "ipd.read"],
        // §12 Nurse row: C on IPD service posting and requisitions, R elsewhere (US6.1).
        // Spec 0024 adds the pre-checkup vitals (US5.3) and the 5A-7 nursing charts.
        ["Nurse"] =
            ["registration.read", "ipd.read", "ipd.service.post",
             "emr.read", "emr.vitals.record", "emr.chart.record"],
        // P4, the first non-operator persona: a consultant writes and signs prescriptions and
        // orders tests from them (US5.4), and reads the patient's record — nothing financial.
        ["OPD Consultant"] =
            ["registration.read", "emr.read", "emr.note.write", "diagnostics.order.create",
             "lis.worklist.read", "ipd.read", "ot.read"],
        // §12 OT column: the in-charge schedules and records; money stays with billing.
        ["OT In-charge"] =
            ["registration.read", "ipd.read", "ot.read", "ot.schedule", "ot.record",
             "pharmacy.read"],
        ["Admin"] =
            ["admin.users.manage", "admin.audit.read", "admin.approvals.decide",
             "admin.masters.manage", "notifications.read"],
        ["MD"] =
            ["dashboard.read", "admin.approvals.decide", "admin.audit.read", "pharmacy.read",
             "ipd.read",
             // §12: the MD approves the payroll lock and sees staff numbers, but does not run payroll.
             "hr.read", "hr.salary.read", "hr.payroll.approve"],
        // §12 Pharmacist row: C on pharmacy, R on patient reg; counter open/close is the money
        // custody the pharmacy POS rides (spec 0016 / ADR-0021 #5).
        ["Pharmacist"] =
            ["registration.read", "pharmacy.read", "pharmacy.sale.create", "pharmacy.purchase.manage",
             "pharmacy.stock.manage", "billing.session.open", "billing.session.close",
             "billing.receipt.create",
             // §12 gives every clinical role `U (own leave)`.
             "hr.leave.apply"],
        // §12 HR Officer row (persona P12, Farid). Salary is a separate grant from `hr.read` on
        // purpose: a department head browses the directory and approves leave without ever seeing
        // what anyone earns.
        ["HR Officer"] =
            ["hr.read", "hr.salary.read", "hr.employee.manage", "hr.attendance.review",
             "hr.roster.manage", "hr.leave.apply", "hr.leave.approve", "hr.payroll.run",
             "hr.policy.manage"],
        ["Department Head"] =
            ["hr.read", "hr.attendance.review", "hr.roster.manage", "hr.leave.apply",
             "hr.leave.recommend"],
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
        ("parvin", "Parvin Akter", "Pharmacist"),
        ("nasrin", "Nasrin Sultana", "Nurse"),
        ("chowdhury", "Dr. A. K. Chowdhury", "OPD Consultant"),
        ("shaheen", "Shaheen Akhter", "OT In-charge"),
        ("moinul", "Moinul Haque", "Radiology Technician"),
        ("farid", "Farid Ahmed", "HR Officer"),                 // persona P12
        ("shirin", "Shirin Begum", "Department Head"),
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
        // Additive, not gated on the block above: a database seeded before spec 0016 has
        // counters but no pharmacy counter — the upgrade gate caught exactly this.
        if (!await bill.Counters.AnyAsync(c => c.Kind == "pharmacy"))
        {
            bill.Counters.Add(new Counter { BranchId = 1, Name = "Pharmacy Counter", Kind = "pharmacy" });
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
            var doctors = new (string name, string room, int max)[]
            {
                ("Dr. Kamrul Hasan", "Room 204 — Medicine", 40),
                ("Dr. Nusrat Jahan", "Room 106 — Gynae & Obs", 30),
                ("Dr. Sohel Rana", "Room 301 — Cardiology", 25),
                ("Dr. Ashraf Ali", "Room 112 — Paediatrics", 35),
            };
            foreach (var (name, room, max) in doctors)
            {
                // The master row first (WP2.5): the schedule carries a foreign key to it, and
                // the id is the identity column's, never a hand-picked constant.
                var doctor = new Doctor { Name = name };
                appt.Doctors.Add(doctor);
                await appt.SaveChangesAsync();
                appt.Schedules.Add(new DoctorSchedule
                {
                    DoctorId = doctor.Id, DoctorName = name, Room = room, MaxSerials = max,
                    Weekday = 0, SlotFrom = new TimeOnly(9, 0), SlotTo = new TimeOnly(14, 0),
                });
            }
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
        // Also refresh templates written before reference bands existed: their parameters carry
        // no bands at all, which would leave result entry unable to judge anything. An upgraded
        // database has to keep working, so the shipped template is re-applied by code.
        var candidates = await adm.TestCatalog.ToListAsync();
        var backfilled = 0;
        foreach (var t in candidates)
        {
            if (t.Template is not null && !ResultTemplates.NeedsUpgrade(t.Template)) continue;
            var template = ResultTemplates.For(t.Code);
            if (template is null) continue;          // imaging/cardiology report a narrative
            t.Template = template;
            backfilled++;
        }
        if (backfilled > 0) await adm.SaveChangesAsync();

        await SeedPharmacyAsync(sp, kdb);
        await SeedIpdAsync(sp, kdb);
        await SeedOtAsync(sp);
        await SeedRadiologyAsync(sp);
    }

    /// <summary>Spec 0017: wards, beds with effective-dated class tariffs, the 5A-9 masters
    /// (admission fee, service charge, package) and the R4/late-post approval policies —
    /// every block is additive so a pre-0017 database upgrades cleanly (ADR-0022 lesson).</summary>
    private static async Task SeedIpdAsync(IServiceProvider sp, KernelDbContext kdb)
    {
        var ipd = sp.GetRequiredService<Hms.Ipd.Data.IpdDbContext>();
        var adm = sp.GetRequiredService<AdmDbContext>();

        if (!await kdb.ApprovalPolicies.AnyAsync(p => p.Type == "folio-late-post"))
        {
            kdb.ApprovalPolicies.AddRange(
                new ApprovalPolicy { Type = "folio-late-post", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "patient-block", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "patient-block", Tier = 2, Role = "MD" },
                new ApprovalPolicy { Type = "patient-release", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "patient-release", Tier = 2, Role = "MD" });
            await kdb.SaveChangesAsync();
        }

        // M16's two ⚿ chains (§11). Both are pure data — the kernel engine needs no HR-specific
        // code to route, escalate or delegate them, which is why §12's "Payroll run lock:
        // HR Officer → Accounts Manager / MD" costs two rows rather than a workflow.
        if (!await kdb.ApprovalPolicies.AnyAsync(p => p.Type == "payroll-lock"))
        {
            kdb.ApprovalPolicies.AddRange(
                new ApprovalPolicy { Type = "leave-approval", Tier = 1, Role = "HR Officer" },
                new ApprovalPolicy { Type = "leave-approval", Tier = 2, Role = "Department Head" },
                new ApprovalPolicy { Type = "payroll-lock", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "payroll-lock", Tier = 2, Role = "MD" });
            await kdb.SaveChangesAsync();
        }

        // IPD catalog services (bed tariffs + 5A-8/5A-9 masters), additive by code.
        var from = new DateOnly(2026, 1, 1);
        var ipdServices = new (string Code, string Name, string Dept, long Price)[]
        {
            ("IPD-BED-GEN", "Bed — General Ward (per day)", "IPD", 1200),
            ("IPD-BED-CAB", "Bed — Cabin (per day)", "IPD", 3500),
            ("IPD-BED-ICU", "Bed — ICU (per day)", "IPD", 8000),
            ("IPD-ADM-FEE", "Admission Fee", "IPD", 500),
            ("IPD-SVC-PCT", "Service Charge (settlement %)", "IPD", 0),
            ("IPD-OXY", "Oxygen (per hour)", "IPD", 150),
            ("IPD-NURS", "Nursing Care (per day)", "IPD", 400),
            ("IPD-VISIT", "Consultant Visit (indoor)", "IPD", 800),
            ("IPD-XBED", "Extra Bed — Attendant (per day)", "IPD", 600),
            ("IPD-VCARD", "Visitor Card Fee", "IPD", 50),
            ("IPD-PKG-ND", "Normal Delivery Package", "IPD", 25000),
        };
        var serviceIds = new Dictionary<string, long>();
        foreach (var (code, name, dept, price) in ipdServices)
        {
            var existing = await adm.Services.SingleOrDefaultAsync(x => x.Code == code);
            if (existing is null)
            {
                existing = new Service { Code = code, Name = name, Dept = dept, Kind = "procedure" };
                adm.Services.Add(existing);
                await adm.SaveChangesAsync();
            }
            // Spec 0023: an item priced at zero is a **priced** item. Skipping the rate version
            // for the percentage-marker service left it looking unpriced, and RUNBOOK §9 step 4
            // ("zero provisional prices") could then never pass — the go-live gate was unclearable.
            // Written outside the creation branch so an upgraded database gains it too.
            if (!await adm.RateVersions.AnyAsync(v =>
                    v.CatalogKind == "service" && v.CatalogId == existing.Id && v.Scope == "standard"))
                adm.RateVersions.Add(new RateVersion
                {
                    BranchId = 1, CatalogKind = "service", CatalogId = existing.Id,
                    Price = price, ValidFrom = from, AuthorId = 1,
                });
            serviceIds[code] = existing.Id;
        }
        await adm.SaveChangesAsync();

        if (!await ipd.Wards.AnyAsync())
        {
            var wards = new[]
            {
                new Hms.Ipd.Data.Ward { BranchId = 1, Name = "General Ward (Male)", Class = "general" },
                new Hms.Ipd.Data.Ward { BranchId = 1, Name = "General Ward (Female)", Class = "general" },
                new Hms.Ipd.Data.Ward { BranchId = 1, Name = "Cabin Block", Class = "cabin" },
                new Hms.Ipd.Data.Ward { BranchId = 1, Name = "ICU", Class = "icu" },
            };
            ipd.Wards.AddRange(wards);
            await ipd.SaveChangesAsync();

            void Beds(Hms.Ipd.Data.Ward ward, string prefix, int count, string tariffCode)
            {
                for (var i = 1; i <= count; i++)
                    ipd.Beds.Add(new Hms.Ipd.Data.Bed
                    {
                        BranchId = 1, WardId = ward.Id, Code = $"{prefix}-{i:D2}",
                        TariffServiceId = serviceIds[tariffCode],
                    });
            }
            Beds(wards[0], "GWM", 4, "IPD-BED-GEN");
            Beds(wards[1], "GWF", 4, "IPD-BED-GEN");
            Beds(wards[2], "CAB", 3, "IPD-BED-CAB");
            Beds(wards[3], "ICU", 2, "IPD-BED-ICU");
            await ipd.SaveChangesAsync();
        }

        if (!await ipd.Packages.AnyAsync())
        {
            ipd.Packages.Add(new Hms.Ipd.Data.AdmissionPackage
            {
                BranchId = 1, Name = "Normal Delivery Package",
                ServiceCatalogId = serviceIds["IPD-PKG-ND"], DefaultServiceChargePct = 5,
            });
            await ipd.SaveChangesAsync();
        }
    }

    /// <summary>Spec 0025: two theatres and a small operation catalogue with the per-role fees,
    /// so M7's workflow is demonstrable rather than hypothetical. Additive and idempotent — a
    /// database seeded before spec 0025 gains the catalogue on its next boot.</summary>
    private static async Task SeedOtAsync(IServiceProvider sp)
    {
        var ot = sp.GetRequiredService<Hms.Ot.Data.OtDbContext>();
        var adm = sp.GetRequiredService<AdmDbContext>();
        var from = new DateOnly(2026, 1, 1);

        var otServices = new (string Code, string Name, long Price)[]
        {
            ("OT-APPY", "Appendicectomy", 22000),
            ("OT-LSCS", "Caesarean Section (LSCS)", 28000),
            ("OT-HERN", "Hernia Repair", 20000),
            ("OT-CHOL", "Cholecystectomy (laparoscopic)", 45000),
            ("OT-FEE-SURG", "Surgeon Fee", 8000),
            ("OT-FEE-ASST", "Assistant Surgeon Fee", 2500),
            ("OT-FEE-ANAE", "Anaesthetist Fee", 4000),
            ("OT-THEATRE", "Theatre Charge", 6000),
        };
        foreach (var (code, name, price) in otServices)
        {
            var existing = await adm.Services.SingleOrDefaultAsync(x => x.Code == code);
            if (existing is null)
            {
                existing = new Service { Code = code, Name = name, Dept = "OT", Kind = "procedure" };
                adm.Services.Add(existing);
                await adm.SaveChangesAsync();
            }
            if (!await adm.RateVersions.AnyAsync(v =>
                    v.CatalogKind == "service" && v.CatalogId == existing.Id && v.Scope == "standard"))
                adm.RateVersions.Add(new RateVersion
                {
                    BranchId = 1, CatalogKind = "service", CatalogId = existing.Id,
                    Price = price, ValidFrom = from, AuthorId = 1,
                });
        }
        await adm.SaveChangesAsync();

        if (!await ot.Theatres.AnyAsync())
        {
            ot.Theatres.AddRange(
                new Hms.Ot.Data.Theatre { BranchId = 1, Name = "OT-1 (General)" },
                new Hms.Ot.Data.Theatre { BranchId = 1, Name = "OT-2 (Obstetric)" });
            await ot.SaveChangesAsync();
        }
    }

    /// <summary>Spec 0026: the modality master and the mapping that turns an imaging order into a
    /// worklist row. Additive and keyed by code, so an upgraded database gains it on next boot.</summary>
    private static async Task SeedRadiologyAsync(IServiceProvider sp)
    {
        var radiology = sp.GetRequiredService<Hms.Radiology.Data.RadiologyDbContext>();
        var adm = sp.GetRequiredService<AdmDbContext>();

        var modalities = new (string Code, string Name, string[] TestCodes)[]
        {
            ("XR", "X-ray", ["XRAY-CH"]),
            ("USG", "Ultrasound", ["USG-ABD"]),
            ("ECG", "ECG / Echo", ["ECG"]),
            ("CT", "CT Scan", []),
            ("MRI", "MRI", []),
        };

        foreach (var (code, name, testCodes) in modalities)
        {
            var modality = await radiology.Modalities.SingleOrDefaultAsync(m => m.Code == code);
            if (modality is null)
            {
                modality = new Hms.Radiology.Data.Modality { BranchId = 1, Code = code, Name = name };
                radiology.Modalities.Add(modality);
                await radiology.SaveChangesAsync();
            }
            foreach (var testCode in testCodes)
            {
                var testId = await adm.TestCatalog.AsNoTracking()
                    .Where(t => t.Code == testCode).Select(t => (long?)t.Id).FirstOrDefaultAsync();
                if (testId is null) continue;
                if (await radiology.ModalityTests.AnyAsync(m => m.TestCatalogId == testId)) continue;
                radiology.ModalityTests.Add(new Hms.Radiology.Data.ModalityTest
                {
                    ModalityId = modality.Id, TestCatalogId = testId.Value,
                });
            }
        }
        await radiology.SaveChangesAsync();
    }

    /// <summary>Spec 0016: enough pharmacy reality for the demo — companies, products, one
    /// supplier, two outlets, and batches that exercise every expiry path (fresh, near-expiry,
    /// expired) so US11.1's blocks are demonstrable, not hypothetical.</summary>
    private static async Task SeedPharmacyAsync(IServiceProvider sp, KernelDbContext kdb)
    {
        var pharm = sp.GetRequiredService<Hms.Pharmacy.Data.PharmDbContext>();
        if (!await kdb.ApprovalPolicies.AnyAsync(p => p.Type == "stock-writeoff"))
        {
            kdb.ApprovalPolicies.AddRange(
                new ApprovalPolicy { Type = "stock-writeoff", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "stock-writeoff", Tier = 2, Role = "MD" },
                new ApprovalPolicy { Type = "purchase-order", Tier = 1, Role = "Billing Supervisor" },
                new ApprovalPolicy { Type = "purchase-order", Tier = 2, Role = "MD" });
            await kdb.SaveChangesAsync();
        }
        if (await pharm.Products.AnyAsync()) return;

        var square = new Hms.Pharmacy.Data.Company { Name = "Square Pharmaceuticals" };
        var beximco = new Hms.Pharmacy.Data.Company { Name = "Beximco Pharma" };
        pharm.Companies.AddRange(square, beximco);
        var supplier = new Hms.Pharmacy.Data.Supplier { Name = "Square Distribution, Sylhet", Phone = "01711-000001" };
        pharm.Suppliers.Add(supplier);
        var main = new Hms.Pharmacy.Data.Outlet { BranchId = 1, Name = "Main Pharmacy", Kind = "main" };
        var sub = new Hms.Pharmacy.Data.Outlet { BranchId = 1, Name = "Emergency Sub-Store", Kind = "sub" };
        pharm.Outlets.AddRange(main, sub);
        await pharm.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        Hms.Pharmacy.Data.PharmProduct P(Hms.Pharmacy.Data.Company c, string brand, string generic,
            string strength, string form, string unit, int rol) => new()
        {
            BranchId = 1, CompanyId = c.Id, Brand = brand, Generic = generic, Strength = strength,
            Form = form, Unit = unit, ReorderLevel = rol, CreatedAt = now, CreatedBy = 1,
        };
        var napa = P(beximco, "Napa", "Paracetamol", "500 mg", "tablet", "pcs", 200);
        var seclo = P(square, "Seclo", "Omeprazole", "20 mg", "capsule", "pcs", 100);
        var fexo = P(square, "Fexo", "Fexofenadine", "120 mg", "tablet", "pcs", 50);
        var amoxSyp = P(square, "Moxacil", "Amoxicillin", "125 mg/5 ml", "syrup", "bottle", 20);
        pharm.Products.AddRange(napa, seclo, fexo, amoxSyp);
        await pharm.SaveChangesAsync();

        Hms.Pharmacy.Data.Batch B(Hms.Pharmacy.Data.PharmProduct p, string no, int addDays, int qty,
            long cost, long mrp) => new()
        {
            BranchId = 1, OutletId = main.Id, ProductId = p.Id, BatchNo = no,
            Expiry = today.AddDays(addDays), QtyOnHand = qty, Cost = cost, Mrp = mrp,
            ReceivedAt = now, ReceivedBy = 1,
        };
        var batches = new[]
        {
            B(napa, "NP-2601", 540, 500, 1, 2),          // fresh
            B(napa, "NP-2412", 45, 80, 1, 2),            // near-expiry: FEFO must pick this first
            B(seclo, "SC-2603", 700, 300, 4, 6),
            B(seclo, "SC-2401", -10, 60, 4, 6),          // expired: sale-blocked, awaiting quarantine
            B(fexo, "FX-2602", 400, 30, 6, 9),           // below its reorder level of 50
            B(amoxSyp, "MX-2605", 300, 40, 28, 40),
        };
        pharm.Batches.AddRange(batches);
        await pharm.SaveChangesAsync();
        foreach (var b in batches)
            pharm.StockMoves.Add(new Hms.Pharmacy.Data.StockMove
            {
                BranchId = 1, OutletId = b.OutletId, BatchId = b.Id, ProductId = b.ProductId,
                Kind = Hms.Pharmacy.Data.MoveKind.Receive, Qty = b.QtyOnHand,
                RefTable = null, RefId = null, Reason = "opening stock (seed)", ActorId = 1, At = now,
            });
        // The seeded opening stock is a purchase on credit — the supplier ledger opens with
        // a real payable so the payments screen has something true to show.
        pharm.SupplierLedger.Add(new Hms.Pharmacy.Data.SupplierLedgerEntry
        {
            BranchId = 1, SupplierId = supplier.Id, Kind = "purchase",
            Amount = batches.Sum(b => (long)b.QtyOnHand * b.Cost),
            Note = "Opening stock (seed)", ActorId = 1, At = now,
        });
        await pharm.SaveChangesAsync();
    }

    private static void ThrowIfFailed(this IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                "Seed failed: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
