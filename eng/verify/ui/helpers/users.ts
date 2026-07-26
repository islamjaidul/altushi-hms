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
    permissions: ["registration.create", "registration.read", "appointments.read", "appointments.create"],
  },
  rasel: {
    password: PASSWORD,
    role: "Billing Operator",
    permissions: [
      "registration.read",
      "billing.invoice.create",
      "billing.receipt.create",
      "billing.session.open",
      "billing.session.close",
      "diagnostics.order.create",
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
    permissions: ["registration.read", "lis.worklist.read", "lis.result.enter", "lis.result.verify"],
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
    permissions: ["dashboard.read", "admin.approvals.decide", "admin.audit.read"],
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
];

export function hasPermission(user: string, permission: string | null): boolean {
  if (permission === null) return true;
  return USERS[user].permissions.includes(permission);
}
