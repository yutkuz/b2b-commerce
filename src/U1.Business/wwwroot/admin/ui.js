import { esc, icon, state } from "../app-core.js?v=20260928a";

export const adminSearch = (placeholder) =>
  /* HTML */ `<label class="search"
      >${icon("search")}<input
        name="q"
        value="${esc(state.params.get("q") || "")}"
        placeholder="${placeholder}"
        aria-label="${placeholder}"
        maxlength="200" /></label
    ><button class="button small" type="submit">Ara</button>`;
export const selectField = (label, name, options, value) =>
  /* HTML */ `<div class="field">
    <label for="field-${name}">${label}</label
    ><select class="input" id="field-${name}" name="${name}">
      ${options.map(([v, l]) => /* HTML */ `<option value="${esc(v)}" ${String(value) === String(v) ? "selected" : ""}>${esc(l)}</option>`).join("")}
    </select>
  </div>`;
