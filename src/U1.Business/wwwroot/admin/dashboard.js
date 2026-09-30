import {
  api,
  esc,
  icon,
  money,
  stock,
  heading,
  empty,
} from "../app-core.js?v=20260928a";
import { renderOrdersTable } from "../orders.js?v=20260930a";

export async function dashboard() {
  const d = await api("/admin/dashboard");
  return (
    heading(
      "Genel bakış",
      "Siparişler, stoklar ve bayi ağı bir arada.",
      /* HTML */ `<a class="button primary" href="#admin-product"
        >${icon("plus")} Ürün ekle</a
      >`,
    ) +
    /* HTML */ `<div class="stats">
        ${[
          ["Ürünler", d.products, "Katalogdaki ürün sayısı", "grid"],
          ["Bayiler", d.users, "Kayıtlı bayi hesabı", "users"],
          ["Bekleyen sipariş", d.pending, "Onayınızı bekliyor", "box"],
          [
            "Onaylanan tutar",
            money(d.revenue),
            "Onaylanan tüm siparişler",
            "chart",
          ],
        ]
          .map(
            ([l, v, s, i]) =>
              /* HTML */ `<article class="stat">
                <div class="stat-label">${l}${icon(i)}</div>
                <strong>${v}</strong><small>${s}</small>
              </article>`,
          )
          .join("")}
      </div>
      <section class="panel">
        <div class="section-title">
          <h2>Son siparişler</h2>
          <a class="text-button" href="#admin-orders">Tümünü görüntüle →</a>
        </div>
        ${renderOrdersTable(d.recentOrders, true)}
      </section>
      <section class="panel" style="margin-top:22px">
        <div class="section-title">
          <h2>Stok takibi</h2>
          <span class="muted"><small>Kritik seviyedeki ürünler</small></span>
        </div>
        ${
          d.lowStock.length
            ? /* HTML */ `<div class="table-scroll">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Ürün kodu</th>
                      <th>Ürün adı</th>
                      <th>Mevcut stok</th>
                      <th>Kritik seviye</th>
                      <th>Durum</th>
                      <th></th>
                    </tr>
                  </thead>
                  <tbody>
                    ${d.lowStock
                      .map(
                        (p) =>
                          /* HTML */ `<tr>
                            <td class="mono">${esc(p.code)}</td>
                            <td>${esc(p.name)}</td>
                            <td>${p.stock}</td>
                            <td>${p.criticalStock}</td>
                            <td>${stock(p)}</td>
                            <td>
                              <a
                                class="row-link"
                                href="#admin-product?id=${p.id}"
                                >Düzenle</a
                              >
                            </td>
                          </tr>`,
                      )
                      .join("")}
                  </tbody>
                </table>
              </div>`
            : empty("Stoklar yeterli.", "Kritik seviyede ürün bulunmuyor.")
        }
      </section>`
  );
}
