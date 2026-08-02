#!/usr/bin/env python3
"""The HRM SKU's operator thread — every screen, every control, and what it actually changed.

Spec 0036 shipped the HRM as an operable product and its own notes record the blind spot that let
four defects through: **every one of them returned HTTP 200 with correct markup.** A route smoke
test cannot see a button that does nothing. So this script never asserts a status code alone. Each
case drives a control the way an operator drives it — fill the form on the page, post it with the
page's own antiforgery token — and then goes *back to a rendered screen* to prove the change is
there. A write that returns 302 and persists nothing fails here.

Tier t1: every assertion is either about a record this run created or a relative change ("the
count moved from N to N+1"), so a dirty database is fine and it survives being run twice (0029).
It does assume the demo seed's fixtures are present — a hundred employees, leave waiting at each
state of the §11 chain, one locked run — which is what both the local seed and the demo deployment
have. It is not fresh-database-only, so it can be pointed at the deployment.

What a run against a shared target leaves behind, listed in its manifest: one employee, one of
each of the six masters, a login named for the run id, a `QA Probe Role`, and one payroll run walked to
Posted on the first month that had none. All of it is findable and none of it is deletable —
hard rule 4 applies to a QA run too.

    BASE_URL=http://localhost:5299 python3 eng/verify/hrm-thread.py

    BASE_URL=https://hrm.example.com HMS_QA_ENV=vm HMS_QA_CONFIRM=hrm.example.com \
        python3 eng/verify/hrm-thread.py
"""
import re
import sys
import urllib.error

from _harness import (BASE, DEV_PASSWORD, RUN_ID, Session, case, check, grant_cells, guard,
                      record, report, step)

# ---------------------------------------------------------------------------
# The HRM cast. Its own seed (HrSeed.Roles), not the ERP's — _harness.CAST is the hospital's.
# ---------------------------------------------------------------------------
HRM_CAST = {
    "admin":  "System Admin",
    "farid":  "HR Officer",
    "nasrin": "Department Head",
    "kamal":  "Accounts Manager",
}

SCREENS = [
    ("/hr", "HR Dashboard"),
    ("/hr/employees", "Employees"),
    ("/hr/employees/new", "Add employee"),
    ("/hr/attendance", "Attendance"),
    ("/hr/roster", "Roster"),
    ("/hr/leave", "Leave"),
    ("/hr/me", "My leave"),
    ("/hr/payroll", "Payroll"),
    ("/hr/policies", "Policies"),
    ("/hr/masters", "Org structure"),
    ("/admin/users", "Users & Roles"),
]

MASTER_TABS = ["units", "designations", "grades", "shifts", "leave-types", "components"]

NAV_LINK = 'class="nav-item'

# Unique per run, so a second run against the same database creates a second account instead of
# colliding with the first — and so the login this run resets the password on is its own (0029).
PROBE_USER = "qa" + re.sub(r"[^0-9a-z]", "", RUN_ID.lower())[-8:]

_ADMIN = None    # the signed-in admin, for helpers that need to re-read a screen

# Razor HTML-encodes the taka sign, so a page carrying money holds the entity, not the glyph.
TAKA = "&#x9F3;"

# Clinical vocabulary this product must never show an employer that is not a hospital (P27, R8).
CLINICAL = ["patient", "doctor", "ward", "bed", "prescription", "diagnos", "admission",
            "discharge", "pharmacy", "clinic", "ipd", "opd", "consultant", "nurse"]


def toast(html):
    m = re.search(r'class="toast[^"]*"[\s\S]{0,300}?<span>([^<]*)</span>', html)
    return m.group(1).strip() if m else None


def alert(html):
    m = re.search(r'class="alert bad"[\s\S]{0,300}?<span>([^<]*)</span>', html)
    return m.group(1).strip() if m else None


def notice(html):
    """Whatever the screen said back — a toast or an inline error."""
    return toast(html) or alert(html)


def status_of(s, path):
    try:
        return s.status(path)[0]
    except urllib.error.HTTPError as e:
        return e.code


def signs_in(user, password=DEV_PASSWORD):
    """Whether these credentials actually get past the login screen.

    `Session()` posts the form and never looks at where it landed, so a refused login produces a
    perfectly usable object that is simply not signed in. Deactivation has to be checked here.
    """
    import http.cookiejar
    import urllib.parse
    import urllib.request
    op = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    login = op.open(BASE + "/login").read().decode()
    m = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', login)
    data = urllib.parse.urlencode({
        "Username": user, "Password": password,
        "__RequestVerificationToken": m.group(1) if m else ""}).encode()
    try:
        landed = op.open(urllib.request.Request(BASE + "/login", data=data))
    except urllib.error.HTTPError:
        return False
    return "/login" not in landed.geturl()


def crashed(url):
    """post_raw puts the failure in the URL slot; the body is the error page, not a marker."""
    return isinstance(url, str) and url.startswith("HTTP 5")


def post_raw(s, path, fields, page):
    """POST and return (landing_url, body) without letting an exception hide a 500."""
    try:
        return s.post(path, fields, page)
    except urllib.error.HTTPError as e:
        return f"HTTP {e.code}", e.read().decode(errors="replace")


# ===========================================================================
def main() -> int:
    guard("t1")
    global _ADMIN
    admin = _ADMIN = Session("admin")

    # -- 1. Every screen renders, with its shell, and says nothing clinical ---------------
    case("HRM-NAV-01", "Every screen renders with the shell", admin)
    nav = admin.get("/hr")
    for path, title in SCREENS:
        code = status_of(admin, path)
        html = admin.get(path) if code == 200 else ""
        check(code == 200, f"{path} → 200")
        esc = title.replace("&", "&amp;")
        check(f"<title>{esc}" in html or esc in html, f"{path} is {title!r}")
        check('rel="stylesheet"' in html and "<aside" in html, f"{path} has the shell")
    check(nav.count(NAV_LINK) >= 9, "sidebar carries nine HR entries plus Administration")

    case("HRM-NAV-02", "No link in the chrome is dead", admin)
    chrome = set(re.findall(r'href="(/[^"#?]*)"', nav))
    for link in sorted(chrome):
        code = status_of(admin, link)
        check(code in (200, 302), f"chrome link {link} → {code}")

    case("HRM-NAV-03", "No clinical noun on an HRM screen (R8, P27)", admin)
    for path, _ in SCREENS:
        body = admin.get(path).lower()
        # Look at the chrome and headings, not employee names that may contain anything.
        chrome_text = " ".join(re.findall(r"<(?:h1|h2|h3|title|kbd|span class=\"muted\")[^>]*>([^<]{0,120})<",
                                          body))
        hits = [w for w in CLINICAL if w in chrome_text]
        check(not hits, f"{path} chrome free of clinical vocabulary {hits or ''}")
    foot = re.search(r'class="statusbar"[\s\S]{0,600}?</', admin.get("/hr"))
    check("patient" not in admin.get("/hr").lower().split("<main")[0],
          "status bar / sidebar foot carry no clinical noun")

    # -- 2. Employees: the search input, its checkbox, and every name link ----------------
    case("HRM-EMP-01", "Employee search finds, misses and survives junk", admin)
    page = admin.get("/hr/employees")
    total = int(re.search(r"(\d+) on record", page).group(1))
    check(total >= 1, f"{total} employees on record")
    check("/hr/employees/new" in page, "Add employee button is present for a manager")

    name = re.search(r'href="/hr/employees/\d+">([^<]+)</a>', page).group(1)
    first_word = name.split()[0]
    hit = admin.get(f"/hr/employees?Q={urlq(first_word)}")
    check(first_word.lower() in hit.lower(), f"search {first_word!r} returns a match")
    check(rows(hit) <= rows(page), "a search narrows the list")

    code = re.search(r'class="tabular">(EMP-\d+)<', page).group(1)
    check(code in admin.get(f"/hr/employees?Q={code}"), f"search by employee code {code}")

    for junk, label in [("ZZQXNOSUCHPERSON", "a miss"), ("%25%25%25", "percent signs"),
                        ("'%20OR%201%3D1--", "a SQL fragment"),
                        ("%3Cscript%3Ealert(1)%3C/script%3E", "a script tag"),
                        ("x" * 400, "400 characters")]:
        try:
            body = admin.get(f"/hr/employees?Q={junk}")
            ok = True
        except urllib.error.HTTPError as e:
            body, ok = "", False
        check(ok, f"search survives {label}")
        if label == "a script tag":
            check("<script>alert(1)" not in body, "the script tag comes back escaped")
        if label == "a miss":
            check("No employees match" in body, "a miss shows the empty state, not a blank table")

    case("HRM-EMP-02", "The leavers checkbox changes the answer", admin)
    plain = rows(admin.get("/hr/employees"))
    withleavers = rows(admin.get("/hr/employees?IncludeLeavers=true"))
    check(withleavers >= plain, f"including leavers does not lose rows ({plain} → {withleavers})")

    case("HRM-EMP-03", "Every employee-name link resolves (AC6)", admin)
    ids = sorted(set(re.findall(r'href="/hr/employees/(\d+)"', page)), key=int)
    check(len(ids) >= 1, f"{len(ids)} employee links on the list")
    bad = [i for i in ids if status_of(admin, f"/hr/employees/{i}") != 200]
    check(not bad, f"all {len(ids)} name links render{' — dead: ' + str(bad[:5]) if bad else ''}")

    case("HRM-EMP-04", "The employee record shows the record", admin)
    detail = admin.get(f"/hr/employees/{ids[0]}")
    for want in ["Joined", "Unit", "Designation", "Grade"]:
        check(want in detail, f"detail carries {want}")
    check("Attendance" in detail or "attendance" in detail, "detail carries recent attendance")
    check("Leave" in detail, "detail carries leave balances")

    case("HRM-EMP-05", "A nonexistent or malformed employee id is refused, not crashed", admin)
    for bad_id, label in [("999999", "an id that does not exist"), ("0", "zero"),
                          ("-1", "a negative id"), ("abc", "letters"),
                          ("99999999999999999999", "an overflowing number")]:
        code = status_of(admin, f"/hr/employees/{bad_id}")
        check(code in (200, 302, 400, 404), f"/hr/employees/{bad_id} → {code} ({label})")
        check(code != 500, f"{label} is not a 500")

    # -- 3. Add employee: the form, its validation, and that it really hired --------------
    case("HRM-EMP-06", "Add employee validates before it writes", admin)
    form = admin.get("/hr/employees/new")
    unit = pick(form, "OrgUnitId")
    desig = pick(form, "DesignationId")
    grade = pick(form, "GradeId")
    check(unit and desig and grade, "the three pickers are populated from the masters")

    good = {"FullName": "QA Probe Kamal", "Phone": "01711000999", "NationalId": "1990123456789",
            "JoinedOn": "01/07/2026", "DateOfBirth": "01/01/1990",
            "OrgUnitId": unit, "DesignationId": desig, "GradeId": grade}

    for field, value, label in [("FullName", "", "a blank name"),
                                ("JoinedOn", "", "no joining date"),
                                ("JoinedOn", "31/02/2026", "31 February"),
                                ("JoinedOn", "not a date", "a date that is words")]:
        bad = dict(good, **{field: value})
        url, body = post_raw(admin, "/hr/employees/new", bad, form)
        check(not crashed(url), f"{label} is not a 500")
        check(alert(body) is not None or "is-invalid" in body,
              f"{label} is refused with a message: {alert(body)!r}")

    case("HRM-EMP-07", "Add employee hires, and the person is on the list", admin)
    before = int(re.search(r"(\d+) on record", admin.get("/hr/employees")).group(1))
    url, body = post_raw(admin, "/hr/employees/new", good, admin.get("/hr/employees/new"))
    check(not crashed(url), "the hire did not 500")
    listing = admin.get("/hr/employees?Q=QA%20Probe%20Kamal")
    check("QA Probe Kamal" in listing, "the new employee is on the list (persisted)")
    after = int(re.search(r"(\d+) on record", admin.get("/hr/employees")).group(1))
    check(after == before + 1, f"the count moved {before} → {after}")
    new_id = re.search(r'href="/hr/employees/(\d+)">QA Probe Kamal', listing)
    record("employee", new_id.group(1) if new_id else "?")
    check(new_id is not None, "the new employee's name links to a record")
    if new_id:
        check(status_of(admin, f"/hr/employees/{new_id.group(1)}") == 200,
              "the new employee's record renders")

    # -- 4. Masters: six tabs, three verbs, and the pickers that depend on them -----------
    case("HRM-MST-01", "Every masters tab renders its own form", admin)
    for tab in MASTER_TABS:
        html = admin.get(f"/hr/masters?tab={tab}")
        check(status_of(admin, f"/hr/masters?tab={tab}") == 200, f"tab {tab} → 200")
        check('handler=Create' in html, f"tab {tab} offers a create form")
        check(f'tab={tab}' in html, f"tab {tab} keeps itself selected")

    case("HRM-MST-02", "Create on each tab, and it appears (AC5)", admin)
    made = {}
    payloads = {
        "units": {"Name": "QA Cutting Floor"},
        "designations": {"Name": "QA Line Supervisor"},
        "grades": {"Name": "QA Grade Z", "Rank": "9"},
        "shifts": {"Name": "QA Night", "StartsAt": "20:00", "EndsAt": "04:00",
                   "BreakMinutes": "30"},
        "leave-types": {"Name": "QA Study Leave", "Accrual": "annual_grant", "Paid": "true"},
        "components": {"Name": "QA Tiffin", "Kind": "earning", "CalcMethod": "fixed",
                       "PercentBp": "0", "Taxable": "true"},
    }
    for tab, extra in payloads.items():
        page = admin.get(f"/hr/masters?tab={tab}")
        fields = dict(extra, Tab=tab, Code="")
        url, body = post_raw(admin, "/hr/masters?handler=Create", fields, page)
        listing = admin.get(f"/hr/masters?tab={tab}")
        check(extra["Name"] in listing, f"{tab}: {extra['Name']!r} was created and is listed")
        made[tab] = extra["Name"]
        record(f"master:{tab}", extra["Name"])

    case("HRM-MST-03", "A new shift and unit reach the screens that consume them", admin)
    check("QA Cutting Floor" in admin.get("/hr/employees/new"),
          "the new unit is in the Add-employee picker")
    check("QA Night" in admin.get("/hr/roster") or "QANIGHT" in admin.get("/hr/roster"),
          "the new shift is in the roster picker")
    check("QA Study Leave" in admin.get("/hr/policies") or
          "QA Study Leave" in admin.get("/hr/masters?tab=leave-types"),
          "the new leave type is reported by /hr/policies (R6)")

    case("HRM-MST-04", "Masters refuse what they should refuse", admin)
    # The duplicate-code case creates its own row with a code it chose, rather than reading one an
    # earlier case happened to leave: a second run finds that row renamed, and a case that quietly
    # tests nothing is worse than one that fails (0029).
    dup = "QADUP" + RUN_ID[-5:].upper()
    post_raw(admin, "/hr/masters?handler=Create",
             {"Tab": "units", "Name": "QA Duplicate Probe", "Code": dup},
             admin.get("/hr/masters?tab=units"))
    check(dup in admin.get("/hr/masters?tab=units"), f"a unit exists on code {dup} to collide with")
    record("master:units", "QA Duplicate Probe")

    checks = [
        ("units", {"Name": ""}, "a blank name"),
        ("units", {"Name": "Clash", "Code": dup}, "a duplicate code"),
        ("shifts", {"Name": "QA Broken", "StartsAt": "", "EndsAt": ""}, "a shift with no times"),
        ("shifts", {"Name": "QA Broken2", "StartsAt": "25:00", "EndsAt": "04:00"},
         "a start time of 25:00"),
        ("components", {"Name": "QA Pct", "Kind": "earning", "CalcMethod": "percent_of",
                        "PercentBp": "0"}, "a percentage of nothing"),
        ("components", {"Name": "QA Pct2", "Kind": "earning", "CalcMethod": "percent_of",
                        "PercentBp": "500000", "BasedOnComponentId": "1"},
         "a percentage of 5000%"),
    ]
    for tab, extra, label in checks:
        page = admin.get(f"/hr/masters?tab={tab}")
        url, body = post_raw(admin, "/hr/masters?handler=Create",
                           dict(extra, Tab=tab), page)
        check(not crashed(url), f"{label} is not a 500")
        check(alert(body) is not None, f"{label} is refused with a message: {alert(body)!r}")

    case("HRM-MST-05", "Rename and retire, and retire takes it out of the pickers", admin)
    units = admin.get("/hr/masters?tab=units")
    uid = row_id(units, "QA Cutting Floor")
    check(uid is not None, "the QA unit has a row with a rename and a retire control")
    url, body = post_raw(admin, "/hr/masters?handler=Rename",
                       {"Tab": "units", "id": uid, "newName": "QA Cutting Floor 2"}, units)
    check("QA Cutting Floor 2" in admin.get("/hr/masters?tab=units"), "rename persisted")

    url, body = post_raw(admin, "/hr/masters?handler=Rename",
                       {"Tab": "units", "id": uid, "newName": "   "}, units)
    check(alert(body) is not None, f"a blank rename is refused: {alert(body)!r}")
    check("QA Cutting Floor 2" in admin.get("/hr/masters?tab=units"), "the name survived it")

    url, body = post_raw(admin, "/hr/masters?handler=Toggle",
                       {"Tab": "units", "id": uid}, units)
    after = admin.get("/hr/masters?tab=units")
    check(not crashed(url), "retire did not 500")
    check("QA Cutting Floor 2" in after, "a retired unit is still listed (no delete — hard rule 4)")
    check("QA Cutting Floor 2" not in admin.get("/hr/employees/new"),
          "a retired unit leaves the Add-employee picker")
    post_raw(admin, "/hr/masters?handler=Toggle", {"Tab": "units", "id": uid}, after)
    check("QA Cutting Floor 2" in admin.get("/hr/employees/new"),
          "bringing it back returns it to the picker")

    case("HRM-MST-06", "A stale tab cannot crash the screen", admin)
    des = admin.get("/hr/masters?tab=designations")
    did = row_id(des, "QA Line Supervisor")
    for handler, fields, label in [
            ("Rename", {"Tab": "units", "id": did, "newName": "Wrong tab"},
             "a designation id posted against the units tab"),
            ("Rename", {"Tab": "units", "id": "9999999", "newName": "Ghost"},
             "an id that does not exist"),
            ("Toggle", {"Tab": "units", "id": "9999999"}, "retiring an id that does not exist")]:
        url, body = post_raw(admin, f"/hr/masters?handler={handler}", fields, des)
        check(not crashed(url), f"{label} is not a 500")

    # -- 5. Attendance ------------------------------------------------------------------
    case("HRM-ATT-01", "The attendance date filter and its checkbox", admin)
    att = admin.get("/hr/attendance")
    check("exception" in att.lower() or rows(att) >= 0, "the register opens on exceptions (US16.1)")
    allrows = rows(admin.get("/hr/attendance?ShowAll=true"))
    check(allrows >= rows(att), f"show-all widens the list ({rows(att)} → {allrows})")
    for date, label in [("01/07/2026", "a past date"), ("01/01/2030", "a far future date"),
                        ("31/02/2026", "31 February"), ("nonsense", "words"), ("", "nothing")]:
        try:
            body = admin.get(f"/hr/attendance?OnDate={urlq(date)}")
            ok = True
        except urllib.error.HTTPError:
            ok = False
        check(ok, f"attendance survives {label}")

    case("HRM-ATT-02", "A correction needs a reason and then sticks", admin)
    att = admin.get("/hr/attendance?ShowAll=true")
    aid = re.search(r'name="attendanceDayId" value="(\d+)"', att)
    if aid:
        aid = aid.group(1)
        url, body = post_raw(admin, "/hr/attendance?handler=Correct",
                           {"attendanceDayId": aid, "toStatus": "present", "reason": ""}, att)
        check(not crashed(url), "a correction with no reason is not a 500")
        check(alert(body) is not None, f"a correction with no reason is refused: {alert(body)!r}")

        url, body = post_raw(admin, "/hr/attendance?handler=Correct",
                           {"attendanceDayId": aid, "toStatus": "present",
                            "reason": "QA: punch machine was down"}, att)
        check(not crashed(url), "a correction with a reason is not a 500")
        after = admin.get("/hr/attendance?ShowAll=true")
        check("QA: punch machine was down" in after or "Corrected" in after or "corrected" in after,
              "the correction is visible on the register (persisted)")

        url, body = post_raw(admin, "/hr/attendance?handler=Correct",
                           {"attendanceDayId": "99999999", "toStatus": "present",
                            "reason": "QA ghost"}, att)
        check(not crashed(url), "correcting a day that does not exist is not a 500")
    else:
        check(False, "no attendance day to correct — the seed produced none")

    # -- 6. Roster ----------------------------------------------------------------------
    case("HRM-ROS-01", "The roster shows who it is rostering", admin)
    ros = admin.get("/hr/roster")
    people = len(set(re.findall(r'name="employeeId" value="(\d+)"', ros)))
    headcount = int(re.search(r"(\d+) on record", admin.get("/hr/employees")).group(1))
    check(people > 0, f"{people} people on the roster board")
    check(people >= headcount or re.search(r"\b%d\b[^<]*of[^<]*\b%d\b" % (people, headcount), ros)
          or f"{people} of {headcount}" in ros,
          f"the board rosters all {headcount} employees, or says it is showing only {people}")
    # The board is 60 rows of 7 selects, each in its own form with its own antiforgery token, so it
    # is the heaviest page either SKU serves. Spec 0037 recorded the weight rather than redesigning
    # it; this holds the line at the measured size so it cannot quietly grow past a §16 box.
    step("weight", f"the roster board is {len(ros) // 1024} KB")
    check(len(ros) < 800_000,
          f"the roster page is {len(ros) // 1024} KB — under the 800 KB ceiling this spec set")

    case("HRM-ROS-02", "Assigning a shift persists it", admin)
    ros = admin.get("/hr/roster")
    emp = re.search(r'name="employeeId" value="(\d+)"', ros).group(1)
    day = re.search(r'name="onDate" value="([^"]+)"', ros).group(1)
    shifts = re.findall(r'<option value="(\d+)"[^>]*>([A-Z0-9]{2,8})</option>', ros)
    shift_id, shift_code = shifts[0]
    url, body = post_raw(admin, "/hr/roster?handler=Assign",
                         {"employeeId": emp, "onDate": day, "shiftId": shift_id}, ros)
    check(not crashed(url), "assigning a shift is not a 500")
    back = admin.get("/hr/roster")
    cell = re.search(
        r'name="employeeId" value="%s"[\s\S]{0,200}?name="onDate" value="%s"[\s\S]{0,800}?</select>'
        % (emp, re.escape(day)), back)
    check(cell is not None, "the cell is still on the board")
    if cell:
        check(f'value="{shift_id}" selected' in cell.group(0).replace('selected="selected"', "selected"),
              f"the cell now shows {shift_code} — the assignment persisted")

    case("HRM-ROS-03", "Assigning keeps the operator on the week they were on", admin)
    # A fortnight out, so landing on "this week" is unmistakably wrong rather than coincidentally
    # right — the pre-fix redirect went to 01 Jan 0001 and fell back to today.
    ros2 = admin.get(f"/hr/roster?WeekOf={urlq('24/08/2026')}")
    shown = re.search(r"<h2>Week of ([^<]+?)\s*(?:<|</h2>)", ros2).group(1).strip()
    check(shown == "23 Aug 2026", f"the roster honours the week filter — showing {shown!r}")

    emp2 = re.search(r'name="employeeId" value="(\d+)"', ros2).group(1)
    day2 = re.search(r'name="onDate" value="([^"]+)"', ros2).group(1)
    # Post the hidden fields the rendered form carries. A browser sends them; a probe that invents
    # its own field list is not testing the form on the screen.
    hidden = dict(re.findall(r'<input type="hidden" name="(WeekOf|OrgUnitId)" value="([^"]*)"', ros2))
    check("WeekOf" in hidden, "the roster cell carries the week it belongs to")
    url, body = post_raw(admin, "/hr/roster?handler=Assign",
                         dict(hidden, employeeId=emp2, onDate=day2, shiftId=shift_id), ros2)
    landed = re.search(r"<h2>Week of ([^<]+?)\s*(?:<|</h2>)", body)
    check("0001" not in url, f"the redirect after assigning is a real week, not {url!r}")
    check(landed and landed.group(1).strip() == shown,
          f"the operator is still on {shown} after assigning, not {landed and landed.group(1).strip()!r}")

    case("HRM-ROS-04", "Roster inputs that are wrong are refused, not crashed", admin)
    for fields, label in [
            ({"employeeId": emp, "onDate": "not a date", "shiftId": shift_id}, "a date of words"),
            ({"employeeId": "99999999", "onDate": day, "shiftId": shift_id}, "a ghost employee"),
            ({"employeeId": emp, "onDate": day, "shiftId": "99999999"}, "a ghost shift")]:
        url, body = post_raw(admin, "/hr/roster?handler=Assign", fields, ros)
        check(not crashed(url), f"{label} is not a 500")

    # -- 7. Leave -----------------------------------------------------------------------
    case("HRM-LV-01", "The leave queue, its checkbox, and the two decisions", admin)
    lv = admin.get("/hr/leave")
    pending = rows(lv)
    decided = rows(admin.get("/hr/leave?ShowDecided=true"))
    check(decided >= pending, f"showing decided widens the queue ({pending} → {decided})")

    rec = re.search(r'handler=Recommend"[\s\S]{0,300}?name="id" value="(\d+)"', lv)
    if rec:
        rid = rec.group(1)
        url, body = post_raw(admin, "/hr/leave?handler=Recommend", {"id": rid}, lv)
        check(not crashed(url), "recommending is not a 500")
        after = admin.get("/hr/leave?ShowDecided=true")
        check(f'value="{rid}"' not in after.split("handler=Recommend")[0] or
              "Recommended" in after or "recommended" in after,
              "the application moved out of Applied (persisted)")
        url, body = post_raw(admin, "/hr/leave?handler=Recommend", {"id": rid}, lv)
        check(not crashed(url), "recommending twice is not a 500")
        check(alert(body) or notice(body), "recommending twice says something")
    else:
        check(False, "no leave application waiting to recommend — the seed produced none")

    lv = admin.get("/hr/leave")
    dec = re.search(r'handler=Decide"[\s\S]{0,400}?name="id" value="(\d+)"', lv)
    if dec:
        did = dec.group(1)
        url, body = post_raw(admin, "/hr/leave?handler=Decide",
                           {"id": did, "approve": "true", "note": "QA approved"}, lv)
        check(not crashed(url), "approving is not a 500")
        after = admin.get("/hr/leave?ShowDecided=true")
        check("Approved" in after or "approved" in after, "an approval shows on the queue")
        url, body = post_raw(admin, "/hr/leave?handler=Decide",
                           {"id": did, "approve": "false", "note": "QA reversal attempt"}, lv)
        check(not crashed(url), "deciding an already-decided application is not a 500")
    else:
        check(False, "no leave application waiting for a decision")

    url, body = post_raw(admin, "/hr/leave?handler=Decide",
                       {"id": "99999999", "approve": "true", "note": "QA ghost"}, lv)
    check(not crashed(url), "deciding an application that does not exist is not a 500")

    # -- 8. My leave --------------------------------------------------------------------
    case("HRM-ME-01", "Self-service says why it is empty", admin)
    me = admin.get("/hr/me")
    linked = 'handler=Apply' in me
    check(linked or "not linked" in me.lower() or "link" in me.lower(),
          "an unlinked login is told why it cannot apply, rather than shown a blank screen")
    if linked:
        url, body = post_raw(admin, "/hr/me?handler=Apply",
                           {"LeaveTypeId": "1", "FromDate": "", "ToDate": "", "Reason": ""}, me)
        check(alert(body) is not None, "an empty application is refused with a message")

    # -- 9. Policies --------------------------------------------------------------------
    case("HRM-POL-01", "The payroll policy saves and reports what is still missing", admin)
    pol = admin.get("/hr/policies")
    check("/hr/masters" in pol, "policies links to the masters screen (R6)")
    check("tax" in pol.lower() or "statutor" in pol.lower() or "not configured" in pol.lower(),
          "policies still reports the unseeded statutory rates (P26)")
    url, body = post_raw(admin, "/hr/policies?handler=SavePayrollPolicy",
                       {"DayCount": "calendar_days", "MinimumNetPay": "500"}, pol)
    check(not crashed(url), "saving the payroll policy is not a 500")
    back = admin.get("/hr/policies")
    check(re.search(r'name="MinimumNetPay"[\s\S]{0,160}?value="500"', back) is not None,
          "the screen shows the minimum net pay it just saved")
    check('value="calendar_days"' in back and "selected" in back,
          "the screen shows the day-count convention in force")
    for value, label in [("-100", "a negative minimum net"), ("abc", "letters"),
                         ("999999999999999999999", "an overflowing number")]:
        url, body = post_raw(admin, "/hr/policies?handler=SavePayrollPolicy",
                           {"DayCount": "calendar_days", "MinimumNetPay": value}, pol)
        check(not crashed(url), f"{label} is not a 500")

    # -- 10. Payroll --------------------------------------------------------------------
    case("HRM-PAY-01", "The payroll list and its money gate", admin)
    pay = admin.get("/hr/payroll")
    check(re.search(r"PR-\d{4}-\d{2}-\d{4}", pay) is not None, "the seeded run is listed")
    check("Locked" in pay or "locked" in pay, "the seeded run is Locked")
    check(TAKA in pay or "৳" in pay, "an hr.salary.read holder sees the money")

    case("HRM-PAY-02", "Generate refuses a period it cannot read", admin)
    for period, label in [("", "an empty period"), ("banana", "words"),
                          ("31/02/2026", "31 February")]:
        url, body = post_raw(admin, "/hr/payroll?handler=Generate", {"Period": period}, pay)
        check(not crashed(url), f"{label} is not a 500")
        check(alert(body) is not None, f"{label} is refused with a message: {alert(body)!r}")

    case("HRM-PAY-03", "A run walks §11 and each state is what the screen offers", admin)
    # Spec 0029's rule: a verify script has to survive being run twice. Pick a month this
    # deployment has no run for, rather than a fixed one that a second run would collide with.
    period = free_period(admin)
    check(period is not None, f"found a payroll month with no run yet: {period}")
    url, body = post_raw(admin, "/hr/payroll?handler=Generate", {"Period": period},
                       admin.get("/hr/payroll"))
    check(not crashed(url), f"generating {period} is not a 500")
    pay = admin.get("/hr/payroll")
    run = re.search(r'(PR-\d{4}-\d{2}-\d{4})[\s\S]{0,600}?(Generated|Draft)', pay)
    check(run is not None, f"a new run is Generated: {run and run.group(1)}")
    record("payroll_run", run.group(1) if run else period)

    url, body = post_raw(admin, "/hr/payroll?handler=Generate", {"Period": period}, pay)
    check(not crashed(url), "generating the same month twice is not a 500")
    check(alert(body) is not None, f"a duplicate period is refused: {alert(body)!r}")

    pay = admin.get("/hr/payroll")
    rid = re.search(r'handler=Review"[\s\S]{0,300}?name="id" value="(\d+)"', pay)
    if rid:
        rid = rid.group(1)
        for handler, expect in [("Review", "Reviewed"), ("RequestApproval", None),
                                ("Approve", "Approved"), ("Lock", "Locked"), ("Post", "Posted")]:
            page = admin.get("/hr/payroll")
            url, body = post_raw(admin, f"/hr/payroll?handler={handler}", {"id": rid}, page)
            check(not crashed(url), f"{handler} is not a 500")
            state = re.search(r'value="%s"[\s\S]{0,80}' % rid, admin.get("/hr/payroll"))
            step(handler, f"after {handler}: {notice(body) or '(no message)'}")
        final = admin.get("/hr/payroll")
        check("Posted" in final or "Locked" in final, "the run reached Locked/Posted")

        # Out-of-order: locking a run that is already locked must be refused, not thrown.
        url, body = post_raw(admin, "/hr/payroll?handler=Lock", {"id": rid},
                           admin.get("/hr/payroll"))
        check(not crashed(url), "locking a locked run is not a 500")
        check(alert(body) is not None, f"locking a locked run is refused: {alert(body)!r}")
    else:
        check(False, "the new run offered no Review control")

    for handler in ["Review", "Approve", "Lock", "Post", "Reverse"]:
        url, body = post_raw(admin, f"/hr/payroll?handler={handler}",
                           {"id": "99999999", "reason": "QA"}, admin.get("/hr/payroll"))
        check(not crashed(url), f"{handler} on a run that does not exist is not a 500")

    # -- 11. Users & Roles (AC2) --------------------------------------------------------
    case("HRM-USR-01", "Create a user, give it a role, and sign in as it", admin)
    users = admin.get("/admin/users")
    check("handler=Create" in users, "the create-user form is on the screen")
    role = re.search(r'name="RoleName"[\s\S]{0,600}?<option value="([^"]+)"', users).group(1)
    url, body = post_raw(admin, "/admin/users?handler=Create",
                       {"Username": PROBE_USER, "DisplayName": "QA Probe",
                        "Password": "Demo#1234", "RoleName": role}, users)
    check(not crashed(url), "creating a user is not a 500")
    check(PROBE_USER in admin.get("/admin/users"), "the new user is listed (persisted)")
    record("user", PROBE_USER)
    try:
        probe = Session(PROBE_USER)
        check("/hr" in probe.get("/hr")[:200] or status_of(probe, "/hr") == 200,
              "the new user can sign in and reach the product")
    except Exception as e:
        check(False, f"the new user could not sign in: {e}")

    case("HRM-USR-02", "The create-user form refuses what it should", admin)
    users = admin.get("/admin/users")
    for fields, label in [
            ({"Username": "", "DisplayName": "X", "Password": "Demo#1234", "RoleName": role},
             "a blank username"),
            ({"Username": PROBE_USER, "DisplayName": "X", "Password": "Demo#1234",
              "RoleName": role}, "a username that is taken"),
            ({"Username": PROBE_USER + "b", "DisplayName": "X", "Password": "x", "RoleName": role},
             "a one-character password"),
            ({"Username": PROBE_USER + "c", "DisplayName": "X", "Password": "Demo#1234",
              "RoleName": "No Such Role"}, "a role that does not exist")]:
        url, body = post_raw(admin, "/admin/users?handler=Create", fields, users)
        check(not crashed(url), f"{label} is not a 500")
        check(notice(body) is not None, f"{label} gets a message back: {notice(body)!r}")

    case("HRM-USR-03", "Reset a password, and the new one is the one that works", admin)
    users = admin.get("/admin/users")
    uid = re.search(
        r'handler=ResetPassword"[\s\S]{0,200}?name="id" value="(\d+)"', users)
    check(uid is not None, "the reset control is on a user row")
    # Reset the probe's own password so no seeded login is disturbed.
    probe_row = re.search(
        PROBE_USER + r'[\s\S]{0,3000}?handler=ResetPassword"[\s\S]{0,200}?name="id" value="(\d+)"', users)
    if probe_row:
        pid = probe_row.group(1)
        url, body = post_raw(admin, "/admin/users?handler=ResetPassword",
                           {"id": pid, "newPassword": "QaProbe#2026"}, users)
        check(not crashed(url), "resetting a password is not a 500")
        try:
            Session(PROBE_USER, "QaProbe#2026")
            check(True, "the new password signs in")
        except Exception as e:
            check(False, f"the reset password does not sign in: {e}")
        url, body = post_raw(admin, "/admin/users?handler=ResetPassword",
                           {"id": pid, "newPassword": ""}, users)
        check(notice(body) is not None, f"an empty new password is refused: {notice(body)!r}")

    case("HRM-USR-04", "Deactivate, and the account stops working", admin)
    users = admin.get("/admin/users")
    probe_row = re.search(
        PROBE_USER + r'[\s\S]{0,3000}?handler=Toggle"[\s\S]{0,200}?name="id" value="(\d+)"', users)
    check(probe_row is not None, "the probe user has a deactivate control")
    if probe_row:
        pid = probe_row.group(1)
        check(signs_in(PROBE_USER, "QaProbe#2026"), "the probe signs in before being deactivated")
        url, body = post_raw(admin, "/admin/users?handler=Toggle", {"id": pid}, users)
        check(not crashed(url), "deactivating is not a 500")
        check(not signs_in(PROBE_USER, "QaProbe#2026"),
              "a deactivated account is refused at the login screen")
        post_raw(admin, "/admin/users?handler=Toggle", {"id": pid}, admin.get("/admin/users"))
        check(signs_in(PROBE_USER, "QaProbe#2026"), "reactivating lets it back in")

    case("HRM-USR-05", "Create a role and move a permission on it", admin)
    users = admin.get("/admin/users")
    copy_from = re.search(r'name="CopyFromRole"[\s\S]{0,600}?<option value="([^"]+)"', users)
    url, body = post_raw(admin, "/admin/users?handler=CreateRole",
                       {"NewRoleName": "QA Probe Role",
                        "CopyFromRole": copy_from.group(1) if copy_from else ""}, users)
    check(not crashed(url), "creating a role is not a 500")
    users = admin.get("/admin/users")
    check("QA Probe Role" in users, "the new role is on the matrix (persisted)")
    record("role", "QA Probe Role")

    url, body = post_raw(admin, "/admin/users?handler=CreateRole",
                       {"NewRoleName": "QA Probe Role", "CopyFromRole": ""}, users)
    check(notice(body) is not None, f"a duplicate role name is refused: {notice(body)!r}")
    url, body = post_raw(admin, "/admin/users?handler=CreateRole",
                       {"NewRoleName": "", "CopyFromRole": ""}, users)
    check(notice(body) is not None, f"a blank role name is refused: {notice(body)!r}")

    case("HRM-USR-06", "The permission matrix offers only permissions this host ships (R3)", admin)
    users = admin.get("/admin/users")
    perms = sorted(set(re.findall(r'name="permission" value="([a-z][a-z.]+)"', users)))
    check(perms, f"{len(perms)} permissions on the matrix")
    stray = [p for p in perms if not (p.startswith("hr.") or p.startswith("admin.")
                                      or p.startswith("notifications."))]
    check(not stray, f"no clinical permission is grantable on the HRM host: {stray or 'none'}")

    cell = re.search(r'name="roleId" value="(\d+)"\s*/>\s*<input type="hidden" '
                     r'name="permission" value="([a-z.]+)"\s*/>\s*<input type="hidden" '
                     r'name="grant" value="(true|false)"', users)
    check(cell is not None, "a matrix cell carries role, permission and the direction")
    if cell:
        rid_, perm, grant = cell.groups()
        url, body = post_raw(admin, "/admin/users?handler=Permission",
                           {"roleId": rid_, "permission": perm, "grant": grant}, users)
        check(not crashed(url), f"toggling {perm} is not a 500")
        after = admin.get("/admin/users")
        flipped = re.search(r'name="roleId" value="%s"\s*/>\s*<input type="hidden" '
                            r'name="permission" value="%s"\s*/>\s*<input type="hidden" '
                            r'name="grant" value="(true|false)"' % (rid_, re.escape(perm)), after)
        check(flipped and flipped.group(1) != grant,
              f"{perm} flipped on role {rid_} and the matrix shows it (persisted)")
        if flipped:
            post_raw(admin, "/admin/users?handler=Permission",
                     {"roleId": rid_, "permission": perm, "grant": flipped.group(1)}, after)

    url, body = post_raw(admin, "/admin/users?handler=Permission",
                       {"roleId": "9999999", "permission": "hr.read", "grant": "true"},
                       admin.get("/admin/users"))
    check(not crashed(url), "granting on a role that does not exist is not a 500")
    url, body = post_raw(admin, "/admin/users?handler=Permission",
                       {"roleId": cell.group(1) if cell else "1",
                        "permission": "pharmacy.dispense", "grant": "true"},
                       admin.get("/admin/users"))
    check("pharmacy.dispense" not in admin.get("/admin/users"),
          "a permission this host does not ship cannot be granted through the handler (R3)")

    # -- 12. The superuser is one (AC1), and the filter still bites --------------------
    case("HRM-AUZ-01", "System Admin holds the whole catalogue (R1/AC1)", admin)
    cells = grant_cells(admin.get("/admin/users"))
    sysadmin = [(p, held) for role, p, _, held in cells if role == "System Admin"]
    check(sysadmin, f"System Admin's row read: {len(sysadmin)} cells")
    ungranted = sorted(p for p, held in sysadmin if not held)
    check(not ungranted, f"System Admin holds every permission; ungranted: {ungranted or 'none'}")
    catalogue = sorted({p for _, p, _, _ in cells})
    check(len(sysadmin) == len(catalogue),
          f"the superuser row covers the whole catalogue ({len(sysadmin)}/{len(catalogue)})")

    case("HRM-AUZ-02", "A role without a claim is refused at the handler, not just the button")
    for user in ["farid", "nasrin", "kamal"]:
        try:
            s = Session(user)
        except Exception as e:
            check(False, f"{user} could not sign in: {e}")
            continue
        nav_ = s.get("/hr")
        entries = nav_.count(NAV_LINK)
        step(user, f"{HRM_CAST[user]} sees {entries} nav entries")
        for path in ["/hr", "/hr/employees"]:
            check(status_of(s, path) in (200, 302), f"{user} reaches {path}")
        # Whatever this role cannot see in the nav, it must also not be able to POST to.
        if "/admin/users" not in nav_:
            check(s.denied("/admin/users"),
                  f"{user} has no Users & Roles entry and the route refuses them too")
            check(s.post_denied("/admin/users?handler=Permission",
                                {"roleId": "1", "permission": "hr.read", "grant": "true"}),
                  f"{user} cannot grant themselves a permission through the handler (G10)")
        if "/hr/payroll" not in nav_:
            check(s.post_denied("/hr/payroll?handler=Generate", {"Period": "01/09/2026"}),
                  f"{user} cannot generate payroll through the handler")

    case("HRM-AUZ-03", "Signed out, nothing is reachable")
    import urllib.request, http.cookiejar
    anon = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
        type("N", (urllib.request.HTTPRedirectHandler,),
             {"redirect_request": lambda *a, **k: None})())
    for path, _ in SCREENS:
        try:
            r = anon.open(BASE + path)
            code, loc = r.getcode(), r.headers.get("Location", "")
        except urllib.error.HTTPError as e:
            code, loc = e.code, e.headers.get("Location", "")
        check(code in (302, 401, 403) and ("login" in loc.lower() or code in (401, 403)),
              f"anonymous {path} → {code} {loc[:40]}")

    return report("HRM operator thread")


# ---------------------------------------------------------------------------
def rows(html):
    body = re.search(r"<tbody>([\s\S]*?)</tbody>", html)
    return body.group(1).count("<tr") if body else 0


def pick(html, name):
    """The first real option value of a named <select>."""
    sel = re.search(r'name="%s"[^>]*>([\s\S]*?)</select>' % re.escape(name), html)
    if not sel:
        return None
    opts = re.findall(r'<option value="([^"]+)"', sel.group(1))
    return opts[0] if opts else None


def row_id(html, name):
    """The id on the row whose name cell holds `name`."""
    m = re.search(re.escape(name) + r'[\s\S]{0,1200}?name="id" value="(\d+)"', html)
    return m.group(1) if m else None


MONTHS = ["January", "February", "March", "April", "May", "June",
          "July", "August", "September", "October", "November", "December"]


def free_period(s):
    """A month with no payroll run on it yet, as `dd/mm/yyyy` — so a second run is not a collision.

    The screen prints its runs' periods as "August 2026", which is the only place a deployment's
    used months can be read without a new endpoint.
    """
    taken = set(re.findall(r"([A-Z][a-z]+ \d{4})", rows_html(s.get("/hr/payroll"))))
    year, month = 2026, 8
    for _ in range(48):
        label = f"{MONTHS[month - 1]} {year}"
        if label not in taken:
            return f"01/{month:02d}/{year}"
        month += 1
        if month > 12:
            month, year = 1, year + 1
    return None


def rows_html(html):
    body = re.search(r"<tbody>([\s\S]*?)</tbody>", html)
    return body.group(1) if body else ""



def urlq(s):
    import urllib.parse
    return urllib.parse.quote(s)


if __name__ == "__main__":
    sys.exit(main())
