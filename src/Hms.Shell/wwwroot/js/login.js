// Login-screen validation, client tier (spec 0040).
//
// The page carries `novalidate`, so the browser no longer refuses the submit with its own
// bubble — which is what previously stopped BOTH tiers of our validation from ever being
// reached, leaving the operator with the browser's wording, in the browser's language, gone by
// the next keystroke. This runs first and says the same thing instantly; the server says it
// again on the round trip and remains authoritative (LoginModel.Validate).
//
// The two message strings are NOT written here: they are rendered into data-msg-* from the C#
// constants, so the instant message and the one that survives a round trip cannot drift.
(function () {
  "use strict";
  const form = document.querySelector("[data-login-form]");
  if (!form) return;

  const fields = [
    { input: form.querySelector("input[name=Username]"),
      message: form.dataset.msgUsername,
      // Mirrors IsNullOrWhiteSpace: spaces are not a username.
      empty: (v) => v.trim() === "" },
    { input: form.querySelector("input[name=Password]"),
      message: form.dataset.msgPassword,
      // Mirrors IsNullOrEmpty, deliberately NOT IsNullOrWhiteSpace: a password of spaces is a
      // password, and refusing it would lock someone out of a credential their account has.
      empty: (v) => v === "" },
  ].filter((f) => f.input && f.message);

  function show(field, message) {
    const box = form.querySelector('[data-error-for="' + field.input.name + '"]');
    if (box) {
      box.textContent = message || "";
      box.hidden = !message;
    }
    field.input.classList.toggle("input-invalid", !!message);
    field.input.setAttribute("aria-invalid", message ? "true" : "false");
  }

  form.addEventListener("submit", function (e) {
    let firstBad = null;
    for (const field of fields) {
      const bad = field.empty(field.input.value);
      show(field, bad ? field.message : null);
      if (bad && !firstBad) firstBad = field.input;
    }
    if (firstBad) {
      // Refuse the submit ourselves. The server would refuse it too — this only saves the
      // operator a round trip on a slow counter PC, it is not the gate.
      e.preventDefault();
      firstBad.focus();
    }
  });

  // Clear a field's complaint as soon as it stops being true. Leaving "Enter your username."
  // under a box that now has a username in it reads as a second, unexplained failure.
  for (const field of fields) {
    field.input.addEventListener("input", function () {
      if (!field.empty(field.input.value)) show(field, null);
    });
  }
})();
