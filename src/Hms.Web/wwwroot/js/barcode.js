// Kernel barcode-wedge listener (05 §3, §7 U6): detects scanner-speed keystroke bursts ending
// with Enter, routes by prefix. Typing stays a fallback on the same fields.
// Prefixes: "P" UHID card → patient context · "S" sample → LIS stage advance · "D" delivery slip.
(function () {
  "use strict";
  const BURST_MS = 40;      // scanners type far faster than humans
  const MIN_LEN = 6;
  let buffer = "";
  let lastKeyAt = 0;

  document.addEventListener("keydown", (e) => {
    const now = Date.now();
    if (now - lastKeyAt > BURST_MS) buffer = "";
    lastKeyAt = now;

    if (e.key === "Enter" && buffer.length >= MIN_LEN) {
      const code = buffer;
      buffer = "";
      // Only treat as a scan if the burst was scanner-fast; otherwise it's normal typing.
      document.dispatchEvent(new CustomEvent("hms:scan", { detail: { code }, bubbles: false }));
      const handler = window.hmsScanRoutes && window.hmsScanRoutes[code[0]];
      if (handler) { e.preventDefault(); handler(code); }
      return;
    }
    if (e.key.length === 1) buffer += e.key;
  });

  // Screens register routes: window.hmsScanRoutes = { S: (code) => advanceSample(code) }
  window.hmsScanRoutes = window.hmsScanRoutes || {};
})();
