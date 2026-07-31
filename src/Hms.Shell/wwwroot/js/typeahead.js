// Kernel type-ahead contract (05 §3, §7 U5): every master reference field uses this module —
// 2+ chars → ranked results from the field's data-source endpoint (same-origin, same authZ),
// arrow/Enter selection, never free-text foreign keys.
// Usage: <input data-typeahead="/registration/search" data-target="#patientId">
(function () {
  "use strict";
  const DEBOUNCE_MS = 150;

  document.addEventListener("input", (e) => {
    const input = e.target;
    if (!(input instanceof HTMLInputElement) || !input.dataset.typeahead) return;
    clearTimeout(input._taTimer);
    input._taTimer = setTimeout(() => query(input), DEBOUNCE_MS);
  });

  async function query(input) {
    const q = input.value.trim();
    const list = ensureList(input);
    if (q.length < 2) { list.hidden = true; return; }
    try {
      const res = await fetch(`${input.dataset.typeahead}?q=${encodeURIComponent(q)}`,
        { headers: { Accept: "application/json" } });
      if (!res.ok) return;
      renderResults(input, list, await res.json());
    } catch { /* offline/latency: keep last results; typing stays usable (U7) */ }
  }

  function ensureList(input) {
    if (input._taList) return input._taList;
    const ul = document.createElement("ul");
    ul.className = "typeahead-results";
    ul.hidden = true;
    input.insertAdjacentElement("afterend", ul);
    input._taList = ul;
    input.addEventListener("keydown", (e) => onKeys(e, input, ul));
    return ul;
  }

  function renderResults(input, ul, items) {
    ul.innerHTML = "";
    items.forEach((item, i) => {
      const li = document.createElement("li");
      li.textContent = item.label;
      li.dataset.value = item.value;
      if (i === 0) li.classList.add("focused");
      li.addEventListener("mousedown", () => choose(input, ul, li));
      ul.appendChild(li);
    });
    ul.hidden = items.length === 0;
  }

  function onKeys(e, input, ul) {
    if (ul.hidden) return;
    const items = [...ul.children];
    const idx = items.findIndex((li) => li.classList.contains("focused"));
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const next = e.key === "ArrowDown" ? Math.min(idx + 1, items.length - 1) : Math.max(idx - 1, 0);
      items.forEach((li, i) => li.classList.toggle("focused", i === next));
    } else if (e.key === "Enter" && idx >= 0) {
      e.preventDefault();
      choose(input, ul, items[idx]);
    } else if (e.key === "Escape") {
      ul.hidden = true;
    }
  }

  function choose(input, ul, li) {
    input.value = li.textContent;
    ul.hidden = true;
    const target = input.dataset.target && document.querySelector(input.dataset.target);
    if (target) target.value = li.dataset.value;
    input.dispatchEvent(new CustomEvent("typeahead:selected",
      { detail: { value: li.dataset.value, label: li.textContent }, bubbles: true }));
    // data-submit: choosing drives the form the way the old onchange-select did (ADR-0020).
    if (input.dataset.submit !== undefined && input.form) input.form.submit();
  }
})();
