// Kernel F-key registry (05 §3). Screens declare their map as data; the footer renders it;
// collisions with the product-wide reserved keys are a CI failure (eng/check-fkeys.sh).
// Reserved product-wide: F2 new patient · F3 item search · F9 hold/recall · F10 payment.
(function () {
  "use strict";
  const RESERVED = { F2: "New Patient", F3: "Item Search", F9: "Hold/Recall", F10: "Payment" };
  const screenMap = {};

  window.hmsFkeys = {
    register(map) {
      // map: { F4: {label, handler} } — reserved keys may only bind their reserved meaning
      Object.assign(screenMap, map);
      render();
    },
    reserved: RESERVED,
  };

  function render() {
    const el = document.getElementById("fkey-map");
    if (!el) return;
    el.textContent = Object.entries(screenMap)
      .map(([k, v]) => `${k} ${v.label}`)
      .join("  ·  ");
  }

  document.addEventListener("keydown", (e) => {
    const entry = screenMap[e.key];
    if (entry && typeof entry.handler === "function") {
      e.preventDefault();
      entry.handler(e);
    }
  });
})();
