// Kernel date-field behaviour (ADR-0020, §7 U13): fields marked data-hms-date accept the
// forgiving formats (mirrors Hms.Kernel.Time.FlexibleDate — the server is authoritative) and
// echo "dd MMM yyyy" on blur so the operator sees the system understood them.
// data-implies="#Range": editing the date flips that period <select> to "custom" — explicit
// dates always win; a silently ignored range is a banned defect class (ADR-0020 #4).
// The paired rule: changing the period <select> away from "custom" clears its dates.
(function () {
  "use strict";
  const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun",
                  "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function parse(raw) {
    const s = raw.trim();
    if (!s) return null;
    let m = /^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})$/.exec(s);        // d/m/yyyy, d-m-yy
    if (m) return build(+m[3], +m[2], +m[1]);
    m = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(s);                        // ISO
    if (m) return build(+m[1], +m[2], +m[3]);
    m = /^(\d{1,2})\s+([A-Za-z]{3,})\.?\s+(\d{4})$/.exec(s);            // 12 Mar 1980
    if (m) {
      const mon = MONTHS.findIndex(x => m[2].toLowerCase().startsWith(x.toLowerCase())) + 1;
      if (mon > 0) return build(+m[3], mon, +m[1]);
    }
    return null;
  }

  function build(y, mo, d) {
    if (y < 100) y += y > 50 ? 1900 : 2000;
    const dt = new Date(Date.UTC(y, mo - 1, d));
    return dt.getUTCFullYear() === y && dt.getUTCMonth() === mo - 1 && dt.getUTCDate() === d
      ? dt : null;
  }

  document.addEventListener("blur", (e) => {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || input.dataset.hmsDate === undefined) return;
    const dt = parse(input.value);
    if (dt) {
      input.value =
        `${String(dt.getUTCDate()).padStart(2, "0")} ${MONTHS[dt.getUTCMonth()]} ${dt.getUTCFullYear()}`;
      input.classList.remove("input-invalid");
    } else if (input.value.trim() !== "") {
      input.classList.add("input-invalid");                 // server still decides (U7)
    }
  }, true);

  document.addEventListener("input", (e) => {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || !input.dataset.implies) return;
    const sel = document.querySelector(input.dataset.implies);
    if (sel instanceof HTMLSelectElement && sel.value !== "custom") sel.value = "custom";
  });

  document.addEventListener("change", (e) => {
    const sel = e.target;
    if (!(sel instanceof HTMLSelectElement)) return;
    const paired = [...document.querySelectorAll("[data-implies]")]
      .filter((input) => input.dataset.implies && document.querySelector(input.dataset.implies) === sel);
    if (paired.length === 0) return;
    if (sel.value !== "custom") {
      // A period choice clears the dates that would otherwise override it, then applies.
      paired.forEach((input) => { input.value = ""; });
      if (sel.dataset.submit !== undefined && sel.form) sel.form.submit();
    }
    // "custom" waits for the operator to type dates and press Apply.
  });
})();
