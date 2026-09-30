import { api, state, heading, pager } from "../app-core.js?v=20260928a";
import { renderOrdersTable } from "../orders.js?v=20260930a";
import { adminSearch } from "./ui.js?v=20260928b";

export async function orderList() {
  const page = +(state.params.get("page") || 1);
  const d = await api("/admin/orders?" + state.params);
  return (
    heading(
      "Sipariş yönetimi",
      "Bayi siparişlerini inceleyin, onaylayın veya reddedin.",
    ) +
    /* HTML */ `<section class="panel">
      <form class="toolbar" data-form="admin-search">
        ${adminSearch("Sipariş numarası veya bayi ara…")}<select
          name="status"
          class="select"
          aria-label="Sipariş durumunu filtrele"
        >
          ${["", "Bekliyor", "Onaylandı", "Hazırlanıyor", "Sevk edildi", "Teslim edildi", "Reddedildi", "İptal edildi"].map((s) => /* HTML */ `<option value="${s}" ${state.params.get("status") === s ? "selected" : ""}>${s || "Tüm durumlar"}</option>`).join("")}
        </select>
      </form>
      ${renderOrdersTable(d.items, true)}${pager(d.total, page)}
    </section>`
  );
}
