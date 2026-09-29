import {
  api,
  esc,
  heading,
  empty,
  pager,
  state,
} from "../app-core.js?v=20260928a";

const ACTIONS = [
  ["", "Tüm işlemler"],
  ["InitialBalance", "Başlangıç bakiyesi"],
  ["ProductCreated", "Ürün oluşturma"],
  ["ManualAdjustment", "Stok düzeltme"],
  ["OrderPlaced", "Sipariş stok düşümü"],
  ["OrderRejected", "Red stok iadesi"],
  ["ProductUpdated", "Ürün güncelleme"],
  ["ProductPriceChanged", "Fiyat değişikliği"],
  ["ProductArchived", "Ürün arşivleme"],
  ["ProductRestored", "Ürünü geri açma"],
  ["CategoryCreated", "Kategori oluşturma"],
  ["CategoryUpdated", "Kategori güncelleme"],
  ["CategoryMerged", "Kategori birleştirme"],
  ["UserUpdated", "Kullanıcı güncelleme"],
  ["UserStatusChanged", "Kullanıcı aktifliği"],
  ["UserPasswordReset", "Parola sıfırlama"],
  ["BannerCreated", "Duyuru oluşturma"],
  ["BannerUpdated", "Duyuru güncelleme"],
  ["OrderStatusChanged", "Sipariş durumu"],
];

const actionLabel = (value) =>
  ACTIONS.find(([key]) => key === value)?.[1] || value;

const value = (name) => esc(state.params.get(name) || "");
const dateTime = (value) => {
  const normalized = value.endsWith("Z") ? value : value + "Z";
  return new Date(normalized).toLocaleString("tr-TR", {
    dateStyle: "short",
    timeStyle: "short",
  });
};

function filters() {
  return /* HTML */ `<form class="panel toolbar" data-form="admin-history-filter">
    <div class="field">
      <label for="history-from">Başlangıç</label>
      <input class="input" id="history-from" name="from" type="datetime-local" value="${value("from")}" />
    </div>
    <div class="field">
      <label for="history-to">Bitiş</label>
      <input class="input" id="history-to" name="to" type="datetime-local" value="${value("to")}" />
    </div>
    <div class="field">
      <label for="history-product">Ürün no</label>
      <input class="input" id="history-product" name="productId" type="number" min="1" value="${value("productId")}" />
    </div>
    <div class="field">
      <label for="history-user">Kullanıcı no</label>
      <input class="input" id="history-user" name="userId" type="number" min="1" value="${value("userId")}" />
    </div>
    <div class="field">
      <label for="history-action">İşlem</label>
      <select class="input" id="history-action" name="action">
        ${ACTIONS.map(([key, label]) => `<option value="${key}" ${state.params.get("action") === key ? "selected" : ""}>${label}</option>`).join("")}
      </select>
    </div>
    <button class="button primary" type="submit">Filtrele</button>
    <a class="button" href="#admin-history">Temizle</a>
  </form>`;
}

function stockChange(item) {
  if (item.kind !== "Stock") return "—";
  const delta = item.quantityDelta > 0 ? `+${item.quantityDelta}` : item.quantityDelta;
  return `<b>${delta}</b><small>${item.previousStock} → ${item.newStock}</small>`;
}

export async function history() {
  const query = new URLSearchParams(state.params);
  const result = await api("/admin/history?" + query);
  return (
    heading(
      "İşlem geçmişi",
      "Stok hareketlerini ve kritik yönetici işlemlerini salt okunur olarak inceleyin.",
    ) +
    filters() +
    `<section class="panel" style="margin-top:22px">
      ${
        result.items.length
          ? `<div class="table-scroll"><table class="data-table">
              <thead><tr><th>Tarih</th><th>İşlem</th><th>Yapan</th><th>Hedef</th><th>Stok</th><th>Açıklama</th></tr></thead>
              <tbody>${result.items
                .map(
                  (item) => `<tr>
                    <td>${dateTime(item.createdAt)}</td>
                    <td><span class="status-badge">${esc(actionLabel(item.action))}</span></td>
                    <td>${esc(item.actor)}${item.actorUserId ? `<small>#${item.actorUserId}</small>` : ""}</td>
                    <td>${esc(item.product || `${item.entityType} #${item.entityId || "—"}`)}</td>
                    <td class="cell-stack">${stockChange(item)}</td>
                    <td>${esc(item.summary)}</td>
                  </tr>`,
                )
                .join("")}</tbody>
            </table></div>`
          : empty("Kayıt bulunamadı.", "Filtreleri değiştirerek tekrar deneyin.")
      }
      ${pager(result.total, result.page)}
    </section>`
  );
}
