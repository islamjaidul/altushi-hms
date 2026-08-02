using System.Text;
using Hms.Hr;
using Hms.Hr.Data;
using Hms.Kernel.Data;
using Hms.Shell;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Web;

/// <summary>
/// A hundred people, three months of attendance, leave at every state of the §11 chain, and one
/// locked payroll run — so the product can be shown doing its job rather than described.
/// <para>
/// Every write goes through the real services: <see cref="EmployeeService.HireAsync"/> issues the
/// codes, <see cref="EmployeeService.SetPayAsync"/> effective-dates the pay, and the payroll run is
/// driven through generate → review → approve → lock exactly as an HR officer would. A seeder that
/// wrote rows directly would produce a database the product itself could not have produced, and
/// would hide precisely the bugs a demo is meant to surface.
/// </para>
/// <para>
/// Deterministic: one fixed <see cref="Random"/> seed, so the same hundred people appear on every
/// install and a screenshot taken today still matches tomorrow. Guarded by <c>Seed:DevUsers</c> and
/// by an employee-count check, so it cannot reach a real install and cannot double-run.
/// </para>
/// <para>
/// <b>On the policy numbers it seeds:</b> the demo employer configures its own absence, overtime,
/// grace, PF, gratuity and tax-band rules — through the same <see cref="PolicyResolver"/> write
/// path the Policies screen uses — because a payroll demo whose every rule table is empty pays
/// full gross to the absent and nothing for overtime, which spec 0038 flagged as AUD-M16-01/02.
/// These are one fictional employer's round numbers, labelled as such; they assert no NBR slab and
/// no Labour Act entitlement (ADR-0027 and P26 stand — statutory leave entitlements remain
/// unseeded). Pay figures are deliberately round.
/// </para>
/// </summary>
public static class HrDemoSeed
{
    private const int HeadCount = 100;
    private const int AttendanceDays = 90;

    /// <summary>Fixed, so the demo is the same demo every time.</summary>
    private const int SeedValue = 20260802;

    private static readonly string[] MaleGiven =
    [
        "Abdul", "Rafiqul", "Kamrul", "Shahidul", "Nazrul", "Jahangir", "Mizanur", "Faruk",
        "Anwar", "Habibur", "Ashraful", "Delwar", "Golam", "Iqbal", "Jasim", "Khalid",
        "Mahbub", "Nasir", "Omar", "Parvez", "Rashed", "Sajjad", "Tanvir", "Zahid",
    ];

    private static readonly string[] FemaleGiven =
    [
        "Rahima", "Shirin", "Nasrin", "Fatema", "Salma", "Ruma", "Taslima", "Nargis",
        "Momtaz", "Rehana", "Sultana", "Jesmin", "Kohinoor", "Laboni", "Marufa", "Nadia",
        "Papia", "Rokeya", "Sabina", "Tahmina",
    ];

    private static readonly string[] Surnames =
    [
        "Islam", "Rahman", "Ahmed", "Hossain", "Chowdhury", "Sarker", "Mia", "Khan",
        "Akter", "Begum", "Uddin", "Ali", "Haque", "Karim", "Mollah", "Talukder",
        "Bhuiyan", "Sikder", "Howlader", "Majumder",
    ];

    private static readonly string[] Banks =
    [
        "Sonali Bank", "Janata Bank", "Agrani Bank", "Dutch-Bangla Bank",
        "BRAC Bank", "Islami Bank Bangladesh", "City Bank", "Pubali Bank",
    ];

    private static readonly string[] BankBranches =
    [
        "Motijheel", "Gulshan", "Dhanmondi", "Uttara", "Mirpur", "Narayanganj", "Gazipur", "Savar",
    ];

    private static readonly string[] BloodGroups = ["A+", "B+", "O+", "AB+", "A-", "B-", "O-"];

    private static readonly (string Code, string Name)[] Units =
    [
        ("PROD", "Production"), ("QC", "Quality Control"), ("STORE", "Stores & Inventory"),
        ("MAINT", "Maintenance"), ("ADMIN", "Administration"), ("ACCT", "Accounts & Finance"),
        ("HR", "Human Resources"), ("SEC", "Security"),
    ];

    private static readonly (string Code, string Name, int Grade)[] Designations =
    [
        ("GM", "General Manager", 0), ("MGR", "Manager", 1), ("AMGR", "Assistant Manager", 2),
        ("EXEC", "Executive", 3), ("SUPV", "Supervisor", 3), ("LINECHF", "Line Chief", 3),
        ("OPRA", "Operator (A)", 4), ("OPRB", "Operator (B)", 4), ("HELPER", "Helper", 5),
        ("QCINSP", "QC Inspector", 3), ("STOREK", "Storekeeper", 4), ("TECH", "Technician", 4),
        ("ELEC", "Electrician", 4), ("ACCT", "Accountant", 3), ("HROFF", "HR Officer", 3),
        ("GUARD", "Security Guard", 5), ("DRIVER", "Driver", 5), ("CLEANER", "Cleaner", 5),
    ];

    /// <summary>Grade rank → monthly basic. Round numbers, obviously a demo (spec 0035).</summary>
    private static readonly (string Code, string Name, int Rank, long Basic)[] Grades =
    [
        ("G1", "Grade 1 — Senior Management", 0, 90_000),
        ("G2", "Grade 2 — Management", 1, 60_000),
        ("G3", "Grade 3 — Assistant Management", 2, 42_000),
        ("G4", "Grade 4 — Officer", 3, 28_000),
        ("G5", "Grade 5 — Skilled", 4, 16_000),
        ("G6", "Grade 6 — General", 5, 11_000),
    ];

    public static async Task RunAsync(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Seed:DevUsers", false)) return;
        if (!config.GetValue("Seed:DemoData", true)) return;

        var tx = sp.GetRequiredService<IHrTx>();
        var employees = sp.GetRequiredService<EmployeeService>();
        var payroll = sp.GetRequiredService<PayrollService>();
        var attendance = sp.GetRequiredService<AttendanceService>();
        var policies = sp.GetRequiredService<PolicyResolver>();
        var clock = sp.GetRequiredService<TimeProvider>();

        const long branchId = 1;
        const long actorId = 1;
        const string actorName = "Demo seed";

        // Dhaka, not UTC: a seed that straddles midnight would put a joining date on the wrong day
        // for six hours a night (spec 0027's defect, in seed form).
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        var already = await tx.RunAsync(s => s.Hr.Employees.CountAsync(e => e.BranchId == branchId));
        if (already > 0) return;

        await tx.RunAsync(async s =>
        {
            await SeedMastersAsync(s.Hr, branchId, today);
            await s.Hr.SaveChangesAsync();
        });

        await tx.RunAsync(async s =>
        {
            await SeedPayRulesAsync(s, policies, branchId, today, actorId, actorName);
        });

        await tx.RunAsync(async s =>
        {
            await SeedPeopleAsync(s, employees, branchId, today, actorId, actorName);
            await s.Hr.SaveChangesAsync();
        });

        await tx.RunAsync(async s =>
        {
            await SeedAttendanceAsync(s.Hr, branchId, today);
            await SeedLeaveAsync(s.Hr, branchId, today, actorId, actorName, clock);
            await s.Hr.SaveChangesAsync();
        });

        // Today's attendance arrives the way a live install's does: raw punches through the
        // import pipeline, then derivation (AUD-M16-08 — this path had never executed against
        // product input; now every fresh install executes it on first boot).
        await tx.RunAsync(async s =>
        {
            await SeedTodayPunchesAsync(s, attendance, branchId, today, actorId, actorName);
        });

        await SeedPayrollAsync(tx, payroll, branchId, today, actorId, actorName);
    }

    private static async Task SeedMastersAsync(HrDbContext hr, long branchId, DateOnly today)
    {
        foreach (var (code, name) in Units)
            hr.OrgUnits.Add(new OrgUnit { BranchId = branchId, Code = code, Name = name });

        foreach (var (code, name, _) in Designations)
            hr.Designations.Add(new Designation { BranchId = branchId, Code = code, Name = name });

        foreach (var (code, name, rank, _) in Grades)
            hr.Grades.Add(new Grade { BranchId = branchId, Code = code, Name = name, Rank = rank });

        hr.Shifts.AddRange(
            new Shift
            {
                BranchId = branchId, Code = "GEN", Name = "General (09:00–18:00)",
                StartsAt = new(9, 0), EndsAt = new(18, 0),
                BreakMinutes = 60, StandardMinutes = 480,
            },
            new Shift
            {
                BranchId = branchId, Code = "MORN", Name = "Morning (06:00–14:00)",
                StartsAt = new(6, 0), EndsAt = new(14, 0),
                BreakMinutes = 30, StandardMinutes = 450,
            },
            // The one that makes the cross-midnight pairing rule visible rather than theoretical.
            new Shift
            {
                BranchId = branchId, Code = "NIGHT", Name = "Night (22:00–06:00)",
                StartsAt = new(22, 0), EndsAt = new(6, 0), EndsNextDay = true,
                BreakMinutes = 30, StandardMinutes = 450,
            });

        // Friday and Saturday. Bangladeshi employers differ, which is exactly why it is data.
        hr.WeeklyOffPatterns.Add(new WeeklyOffPattern
        {
            BranchId = branchId, Name = "Friday & Saturday",
            DayMask = (1 << (int)DayOfWeek.Friday) | (1 << (int)DayOfWeek.Saturday),
            EffectiveFrom = today.AddYears(-2),
        });

        // Leave types are named by this employer, and carry no entitlement numbers — the policy rows
        // that would quantify them are the customer's to write (ADR-0027, P26).
        hr.LeaveTypes.AddRange(
            new LeaveType { BranchId = branchId, Code = "CL", Name = "Casual", Accrual = LeaveAccrual.AnnualGrant },
            new LeaveType { BranchId = branchId, Code = "SL", Name = "Sick", Accrual = LeaveAccrual.AnnualGrant },
            new LeaveType { BranchId = branchId, Code = "EL", Name = "Earned", Accrual = LeaveAccrual.Earned },
            new LeaveType { BranchId = branchId, Code = "ML", Name = "Maternity", Accrual = LeaveAccrual.None },
            new LeaveType { BranchId = branchId, Code = "LWP", Name = "Without pay", Accrual = LeaveAccrual.None, Paid = false });

        await hr.SaveChangesAsync();

        var basic = new PayComponent
        {
            BranchId = branchId, Code = "BASIC", Name = "Basic salary",
            Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
            Taxable = true, PfApplicable = true, DisplayOrder = 10,
        };
        hr.PayComponents.Add(basic);
        await hr.SaveChangesAsync();

        hr.PayComponents.AddRange(
            new PayComponent
            {
                BranchId = branchId, Code = "HRA", Name = "House rent allowance",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.PercentOf,
                BasedOnComponentId = basic.Id, PercentBp = 50_00, Taxable = true, DisplayOrder = 20,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "MED", Name = "Medical allowance",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
                Taxable = false, DisplayOrder = 30,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "CONV", Name = "Conveyance",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
                Taxable = false, DisplayOrder = 40,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "OT", Name = "Overtime",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.OvertimePay, Taxable = true, DisplayOrder = 50,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "ABSENT", Name = "Absence deduction",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.AbsenceDeduction, DisplayOrder = 60,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "LWP", Name = "Leave without pay",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.LeaveWithoutPay, DisplayOrder = 70,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "LATE", Name = "Late arrival deduction",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.LateDeduction, DisplayOrder = 80,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "PF", Name = "Provident fund (employee)",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.ProvidentFund, DisplayOrder = 90,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "TAX", Name = "Income tax",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.IncomeTax, DisplayOrder = 100,
            },
            new PayComponent
            {
                BranchId = branchId, Code = "ARREARS", Name = "Arrears",
                Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.Arrears, Taxable = true, DisplayOrder = 55,
            });

        // The one policy a run cannot start without. Both numbers are this employer's choice, not
        // a statute we are asserting: this employer prorates over a fixed 30 days — the common
        // Bangladeshi payroll convention — and allows no payslip below zero.
        hr.PayrollPolicies.Add(new PayrollPolicy
        {
            BranchId = branchId,
            EffectiveFrom = today.AddYears(-2),
            DayCountConvention = DayCountConvention.Fixed30,
            MinimumNetPayTaka = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = 1,
        });

        await hr.SaveChangesAsync();
    }

    /// <summary>
    /// The demo employer's own pay rules, written through the same effective-dated service path
    /// the Policies screen drives. Round fictional numbers — no NBR slab or Labour Act rate is
    /// asserted (rule 3); an empty-ruled payroll demo would pay the absent in full (AUD-M16-01).
    /// </summary>
    private static async Task SeedPayRulesAsync(
        HrScope s, PolicyResolver policies, long branchId, DateOnly today,
        long actorId, string actorName)
    {
        var from = today.AddYears(-2);

        // One day's pay per absent or unpaid-leave day.
        await policies.SetDeductionAsync(
            s.Hr, s.Kernel, branchId, from, 10_000, 10_000, actorId, actorName);

        // 10 minutes' grace, three free lates a month, half a day's pay per excess late.
        await policies.SetGraceTimeAsync(
            s.Hr, s.Kernel, branchId, from, 10, 3, 5_000, actorId, actorName);

        // Overtime at 2.0x from the first minute, uncapped, paid rather than banked.
        await policies.SetOvertimeAsync(
            s.Hr, s.Kernel, branchId, from, 0, 20_000, 0, false, actorId, actorName);

        // 8% + 8% provident fund after six months' service, no monthly cap.
        await policies.SetPfAsync(
            s.Hr, s.Kernel, branchId, from, 800, 800, 6, null, actorId, actorName);

        // 30 days of pay per completed year, after five years.
        await policies.SetGratuityAsync(
            s.Hr, s.Kernel, branchId, from, 60, 300_000, actorId, actorName);

        // This employer's monthly withholding bands. Fictional and round, like the salaries.
        await policies.SetTaxSlabsAsync(
            s.Hr, s.Kernel, branchId, from,
            [(30_000, 0), (60_000, 500), (100_000, 1_000), (0, 1_500)],
            actorId, actorName);
    }

    private static async Task SeedPeopleAsync(
        HrScope s, EmployeeService service, long branchId, DateOnly today,
        long actorId, string actorName)
    {
        var rng = new Random(SeedValue);

        var units = await s.Hr.OrgUnits.Where(u => u.BranchId == branchId).ToListAsync();
        var designations = await s.Hr.Designations.Where(d => d.BranchId == branchId).ToListAsync();
        var grades = await s.Hr.Grades.Where(g => g.BranchId == branchId).OrderBy(g => g.Rank).ToListAsync();

        var basic = await s.Hr.PayComponents.FirstAsync(c => c.BranchId == branchId && c.Code == "BASIC");
        var hra = await s.Hr.PayComponents.FirstAsync(c => c.BranchId == branchId && c.Code == "HRA");
        var med = await s.Hr.PayComponents.FirstAsync(c => c.BranchId == branchId && c.Code == "MED");
        var conv = await s.Hr.PayComponents.FirstAsync(c => c.BranchId == branchId && c.Code == "CONV");

        for (var i = 0; i < HeadCount; i++)
        {
            // Roughly the shape of a Bangladeshi manufacturing headcount: a wide base, a narrow top.
            var rank = rng.Next(100) switch
            {
                < 2 => 0,
                < 8 => 1,
                < 20 => 2,
                < 42 => 3,
                < 72 => 4,
                _ => 5,
            };
            var grade = grades[rank];
            var (_, _, _, basicPay) = Grades[rank];

            var female = rng.Next(100) < 35;
            var given = female
                ? FemaleGiven[rng.Next(FemaleGiven.Length)]
                : MaleGiven[rng.Next(MaleGiven.Length)];
            var name = $"{given} {Surnames[rng.Next(Surnames.Length)]}";

            var eligible = Designations
                .Select((d, idx) => (d, idx))
                .Where(x => x.d.Grade == rank)
                .ToList();
            var designation = eligible.Count > 0
                ? designations[eligible[rng.Next(eligible.Count)].idx]
                : designations[rng.Next(designations.Count)];

            var joined = today.AddDays(-rng.Next(60, 2600));
            // Probation is six months here — an employer's rule, and the reason a demo has both.
            var confirmed = joined.AddMonths(6) <= today ? joined.AddMonths(6) : (DateOnly?)null;

            var draft = new Employee
            {
                EmployeeCode = "",                       // HireAsync issues it from the number series
                FullName = name,
                FatherName = $"{MaleGiven[rng.Next(MaleGiven.Length)]} {Surnames[rng.Next(Surnames.Length)]}",
                DateOfBirth = joined.AddYears(-rng.Next(20, 45)).AddDays(-rng.Next(365)),
                Gender = female ? "female" : "male",
                NationalId = $"{rng.NextInt64(1_000_000_0000, 9_999_999_9999)}",
                Phone = $"+8801{rng.Next(3, 10)}{rng.Next(10_000_000, 99_999_999)}",
                Email = null,
                PresentAddress = $"House {rng.Next(1, 120)}, Road {rng.Next(1, 30)}, "
                                 + BankBranches[rng.Next(BankBranches.Length)] + ", Dhaka",
                BloodGroup = BloodGroups[rng.Next(BloodGroups.Length)],
                EmergencyContact = $"+8801{rng.Next(3, 10)}{rng.Next(10_000_000, 99_999_999)}",
                JoinedOn = joined,
                ConfirmedOn = confirmed,
                BankName = Banks[rng.Next(Banks.Length)],
                BankBranch = BankBranches[rng.Next(BankBranches.Length)],
                BankAccountNo = $"{rng.NextInt64(1000_0000_0000, 9999_9999_9999)}",
                BankRoutingNo = $"{rng.Next(100, 300)}{rng.Next(100000, 999999)}",
            };

            var employee = await service.HireAsync(
                s.Hr, s.Kernel, branchId, draft,
                units[rng.Next(units.Count)].Id, designation.Id, grade.Id,
                actorId, actorName);

            if (confirmed is not null)
            {
                employee.Status = EmploymentStatus.Confirmed;
                s.Hr.EmploymentEvents.Add(new EmploymentEvent
                {
                    BranchId = branchId, EmployeeId = employee.Id,
                    Kind = EmploymentEventKind.Confirmed, OnDate = confirmed.Value,
                    Note = "Confirmed after probation",
                    RecordedAt = DateTimeOffset.UtcNow, RecordedBy = actorId, RecordedByName = actorName,
                });
            }

            await service.SetPayAsync(
                s.Hr, s.Kernel, branchId, employee.Id,
                new EmployeePayStructure { EffectiveFrom = joined, Reason = "joining" },
                [
                    new EmployeePayComponent { ComponentId = basic.Id, AmountTaka = basicPay },
                    new EmployeePayComponent { ComponentId = hra.Id, AmountTaka = basicPay / 2 },
                    new EmployeePayComponent { ComponentId = med.Id, AmountTaka = 1_000 },
                    new EmployeePayComponent { ComponentId = conv.Id, AmountTaka = 1_500 },
                ],
                actorId, actorName);

            await s.Hr.SaveChangesAsync();
        }
    }

    private static async Task SeedAttendanceAsync(HrDbContext hr, long branchId, DateOnly today)
    {
        var rng = new Random(SeedValue + 1);

        var staff = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId)
            .Select(e => new { e.Id, e.JoinedOn })
            .ToListAsync();
        var generalShift = await hr.Shifts.FirstAsync(s => s.BranchId == branchId && s.Code == "GEN");

        // History starts at yesterday. Today is deliberately NOT written here: it arrives through
        // the punch import pipeline (SeedTodayPunchesAsync), so the screen the product opens on
        // shows days the derivation engine actually produced — and the engine has run on every
        // install, not only in a test (AUD-M16-08).
        foreach (var person in staff)
        foreach (var offset in Enumerable.Range(1, AttendanceDays - 1))
        {
            var day = today.AddDays(-offset);
            if (day < person.JoinedOn) continue;

            var weekday = day.DayOfWeek;
            if (weekday is DayOfWeek.Friday or DayOfWeek.Saturday)
            {
                hr.AttendanceDays.Add(new AttendanceDay
                {
                    BranchId = branchId, EmployeeId = person.Id, OnDate = day,
                    Status = AttendanceStatus.WeeklyOff, PayableFractionBp = 10000,
                    DerivedAt = DateTimeOffset.UtcNow,
                });
                continue;
            }

            var roll = rng.Next(100);
            var (status, late) = roll switch
            {
                < 78 => (AttendanceStatus.Present, 0),
                < 90 => (AttendanceStatus.Present, rng.Next(5, 45)),   // late, but present
                < 94 => (AttendanceStatus.OnLeave, 0),
                < 97 => (AttendanceStatus.Absent, 0),
                // Punched in and never out. These are the pre-listed exceptions US16.1 promises the
                // HR officer before any money is approved — an empty exception list would make the
                // demo look tidier and the feature invisible.
                _ => (AttendanceStatus.Incomplete, 0),
            };

            var inAt = Ui.DhakaMidnightUtc(day).AddHours(9).AddMinutes(late);
            var worked = status == AttendanceStatus.Present ? 480 - late : 0;

            hr.AttendanceDays.Add(new AttendanceDay
            {
                BranchId = branchId, EmployeeId = person.Id, OnDate = day,
                ShiftId = generalShift.Id,
                Status = status,
                FirstIn = status is AttendanceStatus.Present or AttendanceStatus.Incomplete ? inAt : null,
                LastOut = status == AttendanceStatus.Present
                    ? inAt.AddMinutes(worked + generalShift.BreakMinutes)
                    : null,
                WorkedMinutes = worked,
                LateMinutes = late,
                OvertimeMinutes = status == AttendanceStatus.Present && rng.Next(100) < 12
                    ? rng.Next(1, 5) * 30
                    : 0,
                PayableFractionBp = status switch
                {
                    AttendanceStatus.Present => 10000,
                    AttendanceStatus.OnLeave => 10000,
                    _ => 0,
                },
                DerivedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private static async Task SeedLeaveAsync(
        HrDbContext hr, long branchId, DateOnly today, long actorId, string actorName, TimeProvider clock)
    {
        var rng = new Random(SeedValue + 2);

        var staff = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId)
            .Select(e => new { e.Id, e.FullName })
            .ToListAsync();
        var types = await hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == branchId).ToListAsync();
        var year = today.Year;

        // Balances first, so the employee record and the self-service screen have something to show.
        foreach (var person in staff)
        foreach (var type in types.Where(t => t.Accrual != LeaveAccrual.None))
        {
            var entitled = type.Code switch
            {
                "CL" => 10, "SL" => 14, "EL" => 15, _ => 0,
            };
            hr.LeaveBalances.Add(new LeaveBalance
            {
                BranchId = branchId, EmployeeId = person.Id, LeaveTypeId = type.Id,
                LeaveYear = year,
                OpeningBp = entitled * 10000,
                AvailedBp = rng.Next(0, entitled / 2) * 10000,
            });
        }

        // Applications spread across the §11 chain, so the approval screens are not all empty.
        var states = new[]
        {
            LeaveApplicationState.Applied, LeaveApplicationState.Recommended,
            LeaveApplicationState.Approved, LeaveApplicationState.Rejected,
        };
        var applicants = staff.OrderBy(_ => rng.Next()).Take(28).ToList();

        for (var i = 0; i < applicants.Count; i++)
        {
            var person = applicants[i];
            var type = types[rng.Next(types.Count)];
            var state = states[i % states.Length];
            var from = today.AddDays(rng.Next(-25, 20));
            var days = rng.Next(1, 4);

            var decided = state is LeaveApplicationState.Approved or LeaveApplicationState.Rejected;

            hr.LeaveApplications.Add(new LeaveApplication
            {
                BranchId = branchId,
                ApplicationNo = $"LV-{year}-{i + 1:D4}",
                EmployeeId = person.Id,
                LeaveTypeId = type.Id,
                FromDate = from,
                ToDate = from.AddDays(days - 1),
                DaysBp = days * 10000,
                Reason = LeaveReasons[rng.Next(LeaveReasons.Length)],
                State = state,
                RecommendedBy = state == LeaveApplicationState.Applied ? null : actorId,
                RecommendedAt = state == LeaveApplicationState.Applied ? null : clock.GetUtcNow(),
                DecidedBy = decided ? actorId : null,
                DecidedAt = decided ? clock.GetUtcNow() : null,
                DecisionNote = state == LeaveApplicationState.Rejected
                    ? "Line cannot be covered that week."
                    : null,
                AppliedAt = clock.GetUtcNow(),
                AppliedBy = actorId,
                AppliedByName = person.FullName,
            });
        }
    }

    private static readonly string[] LeaveReasons =
    [
        "Family function at home village.", "Fever — will bring a prescription.",
        "Son's school admission.", "Personal work at the union office.",
        "Attending a wedding in Comilla.", "Doctor's appointment for my mother.",
        "House shifting.", "Eid travel — going early to avoid the rush.",
    ];

    /// <summary>
    /// Today's punches, imported through the real pipeline: file rows → <c>ImportAsync</c> →
    /// per-day derivation. Most people punched in and out, a few punched only once (US16.1's
    /// exception list stays honest), and a few did not come in at all.
    /// </summary>
    private static async Task SeedTodayPunchesAsync(
        HrScope s, AttendanceService attendance, long branchId, DateOnly today,
        long actorId, string actorName)
    {
        var rng = new Random(SeedValue + 3);

        var staff = await s.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.SeparatedOn == null)
            .Select(e => e.EmployeeCode)
            .OrderBy(c => c)
            .ToListAsync();

        var rows = new StringBuilder("employee_code, punched_at, direction\n");
        foreach (var code in staff)
        {
            var roll = rng.Next(100);
            if (roll >= 96) continue;                          // absent: no punches at all

            var late = roll is >= 78 and < 90 ? rng.Next(5, 45) : 0;
            var inAt = today.ToDateTime(new TimeOnly(9, 0)).AddMinutes(late);
            rows.Append($"{code}, {inAt:dd/MM/yyyy HH:mm}, in\n");

            if (roll is >= 90 and < 96) continue;              // punched in, never out: incomplete

            var outAt = today.ToDateTime(new TimeOnly(18, 0))
                .AddMinutes(rng.Next(100) < 12 ? rng.Next(1, 5) * 30 : 0);
            rows.Append($"{code}, {outAt:dd/MM/yyyy HH:mm}, out\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rows.ToString()));
        await attendance.ImportAsync(
            s.Hr, s.Kernel, branchId, $"demo-punches-{today:yyyyMMdd}.csv",
            new CsvPunchSource(), stream, actorId, actorName);

        // Punches must be durable before derivation reads them back day by day.
        await s.Hr.SaveChangesAsync();
        var batchId = await s.Hr.PunchImportBatches.MaxAsync(b => b.Id);
        await attendance.DeriveImportedDaysAsync(s.Hr, branchId, batchId);
    }

    /// <summary>
    /// Last month, run for real: generate → review → approve → lock. The state machine and the
    /// balanced-journal check both execute, so a demo that shows a locked run is showing one the
    /// product actually produced. It stops at Locked — `hrm-thread.py` relies on that being the
    /// seed's terminal state, and posting (which issues the payslips) is what the thread itself
    /// drives end to end.
    /// </summary>
    private static async Task SeedPayrollAsync(
        IHrTx tx, PayrollService payroll, long branchId, DateOnly today,
        long actorId, string actorName)
    {
        var period = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);

        try
        {
            var runId = await tx.RunAsync(async s =>
            {
                var run = await payroll.GenerateAsync(
                    s.Hr, s.Kernel, branchId, period, actorId, actorName);
                await s.Hr.SaveChangesAsync();
                await s.Kernel.SaveChangesAsync();
                return run.Id;
            });

            await tx.RunAsync(async s =>
            {
                await payroll.ReviewAsync(s.Hr, s.Kernel, branchId, runId, actorId, actorName);
                await payroll.ApproveAsync(s.Hr, s.Kernel, branchId, runId, actorId, actorName);
                await payroll.LockAsync(s.Hr, s.Kernel, branchId, runId, actorId, actorName);
                await s.Hr.SaveChangesAsync();
                await s.Kernel.SaveChangesAsync();
            });
        }
        catch (HrException)
        {
            // A demo that cannot run payroll is still a usable demo — a startup that dies because of
            // one is not. The screens say what is missing, which is the honest outcome either way.
        }
    }
}
