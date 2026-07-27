// Shell behaviours shared by every screen (05 §3). Everything here degrades gracefully:
// with JS off the pages still post and render — the server is the source of truth (U7).
(function () {
  "use strict";

  // ---- toasts: non-blocking confirmations (§7 U8) ----------------------
  const toast = document.querySelector("[data-toast]");
  if (toast) setTimeout(() => toast.remove(), 3200);

  // ---- "/" focuses the global patient search from anywhere (05 §2) -----
  document.addEventListener("keydown", (e) => {
    if (e.key !== "/" || e.ctrlKey || e.metaKey || e.altKey) return;
    const tag = document.activeElement && document.activeElement.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT") return;
    const search = document.querySelector("[data-global-search]");
    if (search) { e.preventDefault(); search.focus(); search.select(); }
  });

  // ---- Enter advances through a form instead of submitting it (§7 U4) --
  // The primary action stays reachable by Tab; only the last field submits.
  document.addEventListener("keydown", (e) => {
    if (e.key !== "Enter") return;
    const el = e.target;
    if (!(el instanceof HTMLInputElement) || el.type === "submit") return;
    if (!el.form || !el.dataset.advance) return;
    const fields = [...el.form.querySelectorAll("input[data-advance], select[data-advance]")];
    const next = fields[fields.indexOf(el) + 1];
    if (next) { e.preventDefault(); next.focus(); if (next.select) next.select(); }
  });

  // ---- consequence preview before destructive/financial posts (§7 U8) --
  document.addEventListener("submit", (e) => {
    const msg = e.target.dataset && e.target.dataset.confirm;
    if (msg && !window.confirm(msg)) e.preventDefault();
  });

  // ---- print preview modal --------------------------------------------
  const overlay = document.querySelector("[data-print-overlay]");
  if (overlay) {
    const close = () => { window.location.hash = ""; overlay.remove(); };
    overlay.addEventListener("click", (e) => { if (e.target === overlay) close(); });
    document.addEventListener("keydown", (e) => { if (e.key === "Escape") close(); });
    const printBtn = overlay.querySelector("[data-print]");
    if (printBtn) printBtn.addEventListener("click", () => window.print());
  }

  // ---- POS: live totals as the operator types (§8 N1 — no round trip) --
  const pos = document.querySelector("[data-pos]");
  if (pos) {
    const gross = Number(pos.dataset.gross || 0);
    const discount = pos.querySelector("[data-discount]");
    const paid = pos.querySelector("[data-paid]");
    const netOut = pos.querySelector("[data-net-out]");
    const dueOut = pos.querySelector("[data-due-out]");
    const fmt = (n) => "৳ " + n.toLocaleString("en-IN");

    function recalc() {
      const d = Math.max(0, Math.min(gross, Number(discount && discount.value) || 0));
      const net = gross - d;
      const p = Math.max(0, Math.min(net, Number(paid && paid.value) || 0));
      if (netOut) netOut.textContent = fmt(net);
      if (dueOut) dueOut.textContent = fmt(net - p);
    }
    [discount, paid].forEach((el) => el && el.addEventListener("input", recalc));
    recalc();

    // F10 = payment, the product-wide reserved key (05 §3)
    const payBtn = pos.querySelector("[data-pay]");
    if (payBtn && window.hmsFkeys) {
      window.hmsFkeys.register({
        F10: { label: "Payment", handler: () => payBtn.click() },
        F3: { label: "Item Search", handler: () => {
          const s = pos.querySelector(".cat-search"); if (s) s.focus();
        } },
      });
    }
  }

  // ---- F2 opens registration wherever the operator holds the permission -
  const newPatient = document.querySelector("[data-new-patient]");
  if (newPatient && window.hmsFkeys) {
    window.hmsFkeys.register({
      F2: { label: "New Patient", handler: () => { window.location.href = newPatient.href; } },
    });
  }

  // ---- barcode routing: sample scan advances the LIS board (§7 U6) -----
  window.hmsScanRoutes.S = function (code) {
    const card = document.querySelector(`[data-barcode="${code}"] [data-advance-btn]`);
    if (card) card.click();
  };
  window.hmsScanRoutes.P = function (code) {
    window.location.href = "/registration?q=" + encodeURIComponent(code);
  };
})();

// ---- live H/L flags on result entry (§7 U12, 02 §2.2) -------------------
// The server recomputes and stores the flag with the range it used; this is the
// operator's immediate feedback, never the record of truth.
(function () {
  "use strict";
  const inputs = document.querySelectorAll("[data-flag-for]");
  if (!inputs.length) return;

  function paint(input) {
    const out = document.getElementById("flag-" + input.dataset.flagFor);
    if (!out) return;
    const low = parseFloat(input.dataset.low);
    const high = parseFloat(input.dataset.high);
    const value = parseFloat(input.value);
    if (isNaN(value) || isNaN(low) || isNaN(high)) {
      out.className = "flag none"; out.textContent = "—"; return;
    }
    if (value < low) { out.className = "flag low"; out.textContent = "Low"; }
    else if (value > high) { out.className = "flag high"; out.textContent = "High"; }
    else { out.className = "flag none"; out.textContent = "—"; }
  }

  inputs.forEach((input) => { input.addEventListener("input", () => paint(input)); paint(input); });
})();

// ---- split tender + discount reason on the POS (5A-4, §5 M4 [M]) --------
(function () {
  "use strict";
  const toggle = document.getElementById("split-toggle");
  const row2 = document.getElementById("tender-2");
  if (toggle && row2) {
    toggle.addEventListener("click", () => {
      const open = row2.style.display !== "none";
      row2.style.display = open ? "none" : "flex";
      toggle.querySelector("span:last-child") ||
        (toggle.lastChild.textContent = open ? " Split across a second mode" : " Remove the second mode");
      if (!open) { const f = row2.querySelector("input"); if (f) f.focus(); }
    });
    // Keep it open on re-render when a second tender was already entered.
    const paid2 = row2.querySelector("[data-paid2]");
    if (paid2 && Number(paid2.value) > 0) row2.style.display = "flex";
  }

  // The reason box only exists once a discount is actually being given (§7 U7).
  const discount = document.querySelector("[data-discount]");
  const reason = document.getElementById("discount-reason");
  if (discount && reason) {
    const sync = () => { reason.style.display = Number(discount.value) > 0 ? "block" : "none"; };
    discount.addEventListener("input", sync);
    sync();
  }

  // The due line has to account for both tenders, not just the first.
  const pos = document.querySelector("[data-pos]");
  if (pos) {
    const gross = Number(pos.dataset.gross || 0);
    const d = pos.querySelector("[data-discount]");
    const p1 = pos.querySelector("[data-paid]");
    const p2 = pos.querySelector("[data-paid2]");
    const dueOut = pos.querySelector("[data-due-out]");
    if (p2 && dueOut) {
      const recalc = () => {
        const net = gross - Math.max(0, Math.min(gross, Number(d && d.value) || 0));
        const paid = (Number(p1 && p1.value) || 0) + (Number(p2.value) || 0);
        dueOut.textContent = "৳ " + Math.max(0, net - paid).toLocaleString("en-IN");
      };
      [d, p1, p2].forEach((el) => el && el.addEventListener("input", recalc));
      recalc();
    }
  }
})();
