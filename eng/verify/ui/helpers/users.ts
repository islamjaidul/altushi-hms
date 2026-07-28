import path from "node:path";

export const AUTH_DIR = path.join(__dirname, "..", ".auth");
export function authFile(user: string) {
  return path.join(AUTH_DIR, `${user}.json`);
}

const PASSWORD = "Demo#1234";

/** DevSeed.cs §12 role templates + demo cast (07 §1). Keep in sync with src/Hms.Web/DevSeed.cs. */
export const USERS: Record<string, { password: string; role: string; permissions: string[] }> = {
  jashim: {
    password: PASSWORD,
    role: "Receptionist",
    // Spec 0017 (§12 front-desk row + US6.3): the front desk manages beds and admissions.
    permissions: [
      "registration.create", "registration.read", "appointments.read", "appointments.create",
      "ipd.read", "ipd.manage",
    ],
  },
  rasel: {
    password: PASSWORD,
    role: "Billing Operator",
    // Spec 0017 (US6.2): settlement, advances and certificates belong to the billing operator.
    permissions: [
      "registration.read",
      "billing.invoice.create",
      "billing.receipt.create",
      "billing.session.open",
      "billing.session.close",
      "diagnostics.order.create",
      "ipd.read",
      "ipd.settle",
    ],
  },
  ripon: {
    password: PASSWORD,
    role: "Lab Technologist",
    // registration.read added post-audit (finding: role home had < 3 action cards, §12).
    permissions: ["registration.read", "lis.worklist.read", "lis.sample.collect", "lis.result.enter"],
  },
  farhana: {
    password: PASSWORD,
    role: "Pathologist",
    // registration.read + lis.result.enter added post-audit — §12's LIS cell for Pathologist is
    // "A(verify) + C", so entry and verification both belong to the role, not just verification.
    // Spec 0026: one reporting consultant signs lab and imaging alike.
    permissions: [
      "registration.read", "lis.worklist.read", "lis.result.enter", "lis.result.verify",
      "radiology.worklist.read", "radiology.report.write",
    ],
  },
  shahid: {
    password: PASSWORD,
    role: "Billing Supervisor",
    permissions: [
      "registration.read",
      "billing.invoice.create",
      "billing.receipt.create",
      "billing.session.close",
      "admin.approvals.decide",
      "ipd.read",
    ],
  },
  admin: {
    password: PASSWORD,
    role: "Admin",
    permissions: [
      "admin.users.manage",
      "admin.audit.read",
      "admin.approvals.decide",
      "admin.masters.manage",
      "notifications.read",
    ],
  },
  md: {
    password: PASSWORD,
    role: "MD",
    permissions: ["dashboard.read", "admin.approvals.decide", "admin.audit.read", "pharmacy.read", "ipd.read"],
  },
  parvin: {
    password: PASSWORD,
    role: "Pharmacist",
    // Spec 0016 (§12 Pharmacist row): C on pharmacy; counter open/close is the money custody
    // the pharmacy POS rides (ADR-0021 #5).
    permissions: [
      "registration.read",
      "pharmacy.read",
      "pharmacy.sale.create",
      "pharmacy.purchase.manage",
      "pharmacy.stock.manage",
      "billing.session.open",
      "billing.session.close",
      "billing.receipt.create",
    ],
  },
  nasrin: {
    password: PASSWORD,
    role: "Nurse",
    // Spec 0017 (§12 Nurse row / US6.1): posts services and requisitions, never money.
    // Spec 0024 adds pre-checkup vitals (US5.3) and the 5A-7 nursing charts.
    permissions: [
      "registration.read", "ipd.read", "ipd.service.post",
      "emr.read", "emr.vitals.record", "emr.chart.record",
    ],
  },
  chowdhury: {
    password: PASSWORD,
    role: "OPD Consultant",
    // Spec 0024 (P4): the first non-operator persona — writes prescriptions, orders tests,
    // reads the record, touches no money.
    permissions: [
      "registration.read", "emr.read", "emr.note.write", "diagnostics.order.create",
      "lis.worklist.read", "ipd.read", "ot.read",
    ],
  },
  moinul: {
    password: PASSWORD,
    role: "Radiology Technician",
    // Spec 0026 (§12 Radiology column): performs studies, never reports them.
    permissions: ["registration.read", "radiology.worklist.read", "radiology.study.perform"],
  },
  shaheen: {
    password: PASSWORD,
    role: "OT In-charge",
    // Spec 0025 (§12 OT column): schedules and records operations; money stays with billing.
    permissions: [
      "registration.read", "ipd.read", "ot.read", "ot.schedule", "ot.record", "pharmacy.read",
    ],
  },
};

export interface RouteSpec {
  path: string;
  permission: string | null; // null = any authed user, no specific permission gate
  user: string; // a user known to hold `permission`
  title: string | null; // expected .page-title text, or null if not from the nav registry
}

/** Routes to cover, one permitted user each (env brief's route table). Titles come straight off
 * ModuleNav.Registry / each page's ViewData["Title"] so a rename there fails this suite too. */
export const ROUTES: RouteSpec[] = [
  { path: "/", permission: null, user: "jashim", title: "Home" },
  { path: "/denied", permission: null, user: "jashim", title: "Not allowed" },
  { path: "/dashboard", permission: "dashboard.read", user: "md", title: "MD Dashboard" },
  { path: "/registration", permission: "registration.read", user: "jashim", title: "Patient Directory" },
  { path: "/registration/new", permission: "registration.create", user: "jashim", title: "New Patient" },
  { path: "/registration/1/card", permission: "registration.read", user: "jashim", title: "Patient ID Card" },
  { path: "/appointments", permission: "appointments.read", user: "jashim", title: "Serials / Queue" },
  { path: "/billing/session", permission: "billing.session.open", user: "rasel", title: "Counter Session" },
  { path: "/billing/opd", permission: "billing.invoice.create", user: "rasel", title: "OPD Invoice" },
  { path: "/billing/dues", permission: "billing.receipt.create", user: "rasel", title: "Due Collection" },
  { path: "/billing/day-close", permission: "billing.session.close", user: "rasel", title: "Counter Day-Close" },
  { path: "/billing/invoice/1", permission: "registration.read", user: "rasel", title: "Money Receipt" },
  { path: "/billing/statement/1", permission: "billing.session.close", user: "rasel", title: "Day-Close Statement" },
  { path: "/diagnostics/order", permission: "diagnostics.order.create", user: "rasel", title: "Diagnostic Order" },
  { path: "/diagnostics/delivery", permission: "diagnostics.order.create", user: "rasel", title: "Report Delivery" },
  { path: "/lis/board", permission: "lis.worklist.read", user: "ripon", title: "Work Board" },
  { path: "/lis/results", permission: "lis.result.enter", user: "ripon", title: "Result Entry" },
  { path: "/lis/verify", permission: "lis.result.verify", user: "farhana", title: "Verification Queue" },
  { path: "/lis/report/1", permission: "lis.worklist.read", user: "ripon", title: "Investigation Report" },
  { path: "/notifications/tray", permission: "notifications.read", user: "admin", title: "SMS Tray" },
  { path: "/admin/approvals", permission: "admin.approvals.decide", user: "shahid", title: "Approvals Inbox" },
  { path: "/admin/users", permission: "admin.users.manage", user: "admin", title: "Users & Roles" },
  { path: "/admin/masters", permission: "admin.masters.manage", user: "admin", title: "Price List & Catalog" },
  { path: "/admin/audit", permission: "admin.audit.read", user: "admin", title: "Audit Viewer" },

  // spec 0013 additions. Note: each page's own ViewData["Title"] (rendered in .page-title) is not
  // always identical to its shorter nav-sidebar label — same pattern already used by document
  // pages elsewhere in this suite (_Layout.cshtml gives ViewData["Title"] priority over the nav
  // registry's label when both are set).
  { path: "/billing/refund", permission: "billing.receipt.create", user: "rasel", title: "Refund & Cancellation" },
  { path: "/billing/reports", permission: "billing.session.close", user: "rasel", title: "Collection & Income Reports" },
  { path: "/admin/import", permission: "admin.masters.manage", user: "admin", title: "Price List & Catalog Import" },
  { path: "/admin/people", permission: "admin.masters.manage", user: "admin", title: "Doctors, Referrers & Consultants" },

  // spec 0016 — M11 Pharmacy.
  { path: "/pharmacy/pos", permission: "pharmacy.sale.create", user: "parvin", title: "Pharmacy Sale" },
  { path: "/pharmacy/stock", permission: "pharmacy.stock.manage", user: "parvin", title: "Stock & Expiry" },
  { path: "/pharmacy/purchase", permission: "pharmacy.purchase.manage", user: "parvin", title: "Purchase Orders" },
  { path: "/pharmacy/products", permission: "pharmacy.purchase.manage", user: "parvin", title: "Products & Companies" },
  { path: "/pharmacy/transfers", permission: "pharmacy.stock.manage", user: "parvin", title: "Outlet Transfers" },
  { path: "/pharmacy/suppliers", permission: "pharmacy.purchase.manage", user: "parvin", title: "Suppliers & Ledger" },
  { path: "/pharmacy/reports", permission: "pharmacy.read", user: "parvin", title: "Pharmacy Reports" },
  { path: "/pharmacy/dashboard", permission: "pharmacy.read", user: "parvin", title: "Pharmacy Dashboard" },

  // spec 0017 — M6 IPD & folio (+R4).
  { path: "/ipd/board", permission: "ipd.read", user: "jashim", title: "Ward Board" },
  { path: "/ipd/admit", permission: "ipd.manage", user: "jashim", title: "New Admission" },
  { path: "/ipd/admissions", permission: "ipd.read", user: "jashim", title: "Admissions & Census" },
  { path: "/ipd/indents", permission: "ipd.service.post", user: "nasrin", title: "Ward Indents" },
  { path: "/ipd/certificates", permission: "ipd.settle", user: "rasel", title: "Certificates" },
  { path: "/ipd/reports", permission: "ipd.read", user: "jashim", title: "IPD Reports" },
  { path: "/pharmacy/indents", permission: "pharmacy.stock.manage", user: "parvin", title: "Indoor Issue Queue" },
  { path: "/emr/queue", permission: "emr.read", user: "chowdhury", title: "Consultation Queue" },
  { path: "/emr/vitals", permission: "emr.vitals.record", user: "nasrin", title: "Pre-checkup Vitals" },
  { path: "/emr/history", permission: "emr.read", user: "chowdhury", title: "Patient Record" },
  { path: "/emr/templates", permission: "emr.note.write", user: "chowdhury", title: "My Templates" },
  { path: "/emr/charts", permission: "emr.chart.record", user: "nasrin", title: "Nursing Charts" },
  { path: "/radiology/worklist", permission: "radiology.worklist.read", user: "moinul", title: "Modality Worklist" },
  { path: "/radiology/modalities", permission: "radiology.study.perform", user: "moinul", title: "Machines & Mapping" },
  { path: "/ot/board", permission: "ot.read", user: "shaheen", title: "OT Board" },
  { path: "/ot/schedule", permission: "ot.schedule", user: "shaheen", title: "Schedule an Operation" },
  { path: "/ot/register", permission: "ot.read", user: "shaheen", title: "Operation Register" },
  { path: "/ot/theatres", permission: "ot.schedule", user: "shaheen", title: "Theatres" },

  // spec 0018 — M2 Front Desk.
  { path: "/frontdesk", permission: "ipd.read", user: "jashim", title: "Help Desk" },

  // spec 0031 (QA finding F5 / LC-XCUT-13) — the eleven protected routes no UI test loaded.
  // They are disproportionately the consequential screens: the folio, the discharge gate, the
  // prescription, the signed report, the amend path. Permission and title are taken from each
  // page's own `@page` directive and `[Authorize]` attribute, never derived from the file path
  // — deriving a route from its path is what produced fifty false permission leaks in 0028.
  // Detail screens take a route parameter; id 1 exists on any seeded database, the same
  // assumption /billing/invoice/1 and /lis/report/1 have always made here.
  // Folio and discharge title themselves after the patient, so their title is not fixed.
  { path: "/ipd/folio/1", permission: "ipd.read", user: "jashim", title: null },
  { path: "/ipd/discharge/1", permission: "ipd.read", user: "jashim", title: null },
  { path: "/emr/consult/1", permission: "emr.note.write", user: "chowdhury", title: "Consultation" },
  { path: "/emr/prescription/1", permission: "emr.read", user: "chowdhury", title: "Prescription" },
  { path: "/radiology/report/1", permission: "radiology.report.write", user: "farhana", title: "Radiology Report" },
  { path: "/radiology/print/1", permission: "radiology.worklist.read", user: "moinul", title: "Radiology Report" },
  { path: "/ot/case/1", permission: "ot.record", user: "shaheen", title: "Operation" },
  { path: "/lis/amend", permission: "lis.result.verify", user: "farhana", title: "Amend a Released Report" },
  { path: "/diagnostics/order/1", permission: "diagnostics.order.create", user: "rasel", title: "Test Order & Labels" },
  { path: "/admin/sms", permission: "admin.masters.manage", user: "admin", title: "SMS Templates" },
  { path: "/admin/templates", permission: "admin.masters.manage", user: "admin", title: "Report Templates" },
];

/** Document (`.sheet`) pages — 05 §6 / U10, and the print-CSS check. */
export const DOCUMENT_ROUTES = [
  "/registration/1/card",
  "/billing/invoice/1",
  "/lis/report/1",
  "/billing/statement/1",
];

/** At least 6 route/user pairs where the user LACKS the permission — §12 / G10 authorisation. */
export const DENIED_PAIRS: { path: string; user: string; permission: string }[] = [
  { path: "/dashboard", user: "jashim", permission: "dashboard.read" },
  { path: "/admin/users", user: "jashim", permission: "admin.users.manage" },
  { path: "/billing/opd", user: "ripon", permission: "billing.invoice.create" },
  { path: "/registration", user: "admin", permission: "registration.read" },
  { path: "/appointments", user: "farhana", permission: "appointments.read" },
  { path: "/admin/audit", user: "rasel", permission: "admin.audit.read" },
  { path: "/lis/board", user: "md", permission: "lis.worklist.read" },
  { path: "/notifications/tray", user: "jashim", permission: "notifications.read" },

  // spec 0013 additions.
  { path: "/billing/refund", user: "ripon", permission: "billing.receipt.create" },
  { path: "/billing/reports", user: "jashim", permission: "billing.session.close" },
  { path: "/admin/import", user: "rasel", permission: "admin.masters.manage" },
  { path: "/admin/people", user: "shahid", permission: "admin.masters.manage" },

  // spec 0016 — pharmacy is the pharmacist's, not the billing floor's.
  { path: "/pharmacy/pos", user: "rasel", permission: "pharmacy.sale.create" },
  { path: "/pharmacy/stock", user: "shahid", permission: "pharmacy.stock.manage" },
  { path: "/pharmacy/purchase", user: "jashim", permission: "pharmacy.purchase.manage" },

  // spec 0017 — the nurse never touches money or admissions; the lab never reaches the ward.
  { path: "/ipd/admit", user: "nasrin", permission: "ipd.manage" },
  { path: "/ipd/certificates", user: "nasrin", permission: "ipd.settle" },
  { path: "/ipd/board", user: "ripon", permission: "ipd.read" },
];

export function hasPermission(user: string, permission: string | null): boolean {
  if (permission === null) return true;
  return USERS[user].permissions.includes(permission);
}
