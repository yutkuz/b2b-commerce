import {
  api,
  state,
  esc,
  icon,
  money,
  stock,
  thumb,
  heading,
  empty,
  field,
  pager,
  toast,
  go,
} from "../app-core.js?v=20260928a";
import { adminSearch, selectField } from "./ui.js?v=20260928b";

const num = (data, keys) =>
  keys.forEach((key) => (data[key] = Number(data[key])));

export async function products() {
  const page = +(state.params.get("page") || 1);
  const d = await api(
    "/products?" +
      new URLSearchParams({ q: state.params.get("q") || "", page }),
  );
  return (
    heading(
      "Ürün yönetimi",
      "Ürün bilgilerini, fiyatları ve stokları güncelleyin.",
      /* HTML */ `<a class="button primary" href="#admin-product"
        >${icon("plus")} Yeni ürün</a
      >`,
    ) +
    /* HTML */ `<section class="panel">
      <form class="toolbar" data-form="admin-search">
        ${adminSearch("Ürün adı, kodu veya marka ara…")}
      </form>
      ${
        d.items.length
          ? /* HTML */ `<div class="table-scroll">
              <table class="data-table">
                <thead>
                  <tr>
                    <th></th>
                    <th>Ürün / Kod</th>
                    <th>Marka</th>
                    <th>Stok</th>
                    <th>Kritik seviye</th>
                    <th>Fiyat</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  ${d.items
                    .map(
                      (p) =>
                        /* HTML */ `<tr>
                          <td>${thumb(p)}</td>
                          <td class="cell-stack">
                            <b>${esc(p.name)}</b
                            ><small class="mono">${esc(p.code)}</small>
                          </td>
                          <td>${esc(p.brand)}</td>
                          <td>${p.stock} adet<br />${stock(p)}</td>
                          <td>${p.criticalStock}</td>
                          <td class="money">${money(p.price)}</td>
                          <td>
                            <a
                              class="button small"
                              href="#admin-product?id=${p.id}"
                              >${icon("edit")} Düzenle</a
                            >
                          </td>
                        </tr>`,
                    )
                    .join("")}
                </tbody>
              </table>
            </div>`
          : empty("Ürün bulunamadı.", "Arama kelimenizi değiştirin.")
      }${pager(d.total, page)}
    </section>`
  );
}

export async function productForm() {
  const id = state.params.get("id");
  const p = id
    ? await api("/products/" + id)
    : {
        code: "",
        name: "",
        description: "",
        brand: "",
        manufacturerCode: "",
        specialCode1: "",
        specialCode2: "",
        imageUrl: "/images/product.svg",
        stock: 0,
        criticalStock: 5,
        price: "",
        categoryId: 1,
      };
  return (
    heading(
      id ? "Ürünü düzenle" : "Yeni ürün ekle",
      "Ürün alanlarını doldurun ve kataloğa kaydedin.",
      /* HTML */ `<a class="button" href="#admin-products">Ürünlere dön</a>`,
    ) +
    /* HTML */ `<form
      data-form="product"
      data-id="${id || ""}"
      data-version="${esc(p.rowVersion || "")}"
    >
      <div class="product-form">
        <section class="panel form-card">
          <h2>Ürün bilgileri</h2>
          <div class="form-grid" style="margin-top:23px">
            ${field("Ürün kodu", "code", p.code, "text", 'required maxlength="60"')}${field("Üretici kodu", "manufacturerCode", p.manufacturerCode, "text", 'required maxlength="80"')}
            <div class="full">
              ${field("Ürün adı", "name", p.name, "text", 'required maxlength="180"')}
            </div>
            ${field("Marka", "brand", p.brand, "text", 'required maxlength="80"')}${selectField(
              "Kategori",
              "categoryId",
              state.meta.categories.map((c) => [c.id, c.name]),
              p.categoryId,
            )}${field("Özel kod 1", "specialCode1", p.specialCode1, "text", 'maxlength="80"')}${field("Özel kod 2", "specialCode2", p.specialCode2, "text", 'maxlength="80"')}
            <div class="field full">
              <label for="description">Açıklama</label
              ><textarea
                name="description"
                id="description"
                class="input"
                required
                maxlength="3000"
                rows="4"
              >
${esc(p.description)}</textarea>
            </div>
            ${field("Birim fiyat (TL)", "price", p.price, "number", 'min="0.01" max="99999999" step="0.01" required')}${field("Stok miktarı", "stock", p.stock, "number", 'min="0" max="1000000" step="1" required')}${field("Kritik stok seviyesi", "criticalStock", p.criticalStock, "number", 'min="0" max="1000000" step="1" required')}
          </div>
          <p class="muted">
            <small
              >Stok miktarı kritik seviyeye eşit veya altındaysa sarı, sıfırsa
              kırmızı gösterilir.</small
            >
          </p>
        </section>
        <aside class="panel form-card">
          <h2>Ürün görseli</h2>
          <img
            class="upload-preview"
            src="${esc(p.imageUrl)}"
            alt="Ürün görseli önizleme"
          />
          <div class="field">
            <label for="product-image-file">Görsel yükle</label
            ><input
              type="file"
              id="product-image-file"
              accept="image/png,image/jpeg,image/webp"
            /><small>PNG, JPEG veya WebP · en fazla 4 MB</small>
          </div>
          ${field("Görsel adresi", "imageUrl", p.imageUrl, "text", 'required maxlength="500"')}
          <p class="muted">
            <small
              >Yüklediğiniz görselin adresi otomatik doldurulur. HTTPS görsel
              bağlantısı da kullanabilirsiniz.</small
            >
          </p>
        </aside>
      </div>
      <div class="form-actions">
        <a class="button" href="#admin-products">Vazgeç</a
        ><button class="button primary" type="submit">
          ${icon("check")} Ürünü kaydet
        </button>
      </div>
    </form>`
  );
}

export async function submitProduct(form, data) {
  num(data, ["categoryId", "price", "stock", "criticalStock"]);
  const id = form.dataset.id;
  if (id) data.version = form.dataset.version;
  try {
    await api("/admin/products" + (id ? "/" + id : ""), {
      method: id ? "PUT" : "POST",
      body: data,
    });
  } catch (error) {
    if (error.code === "PRODUCT_CHANGED") {
      let notice = form.querySelector(".product-conflict");
      if (!notice) {
        notice = document.createElement("div");
        notice.className = "notice product-conflict";
        notice.setAttribute("role", "alert");
        form.prepend(notice);
      }
      notice.innerHTML =
        'Ürün başka bir işlemde değişti. Girdiğiniz alanlar korunuyor. Güncel ürünü görmek için <button type="button" class="button small" data-action="reload-product">Yeniden yükle</button>.';
      notice.querySelector("button").focus();
      return;
    }
    throw error;
  }
  state.meta = null;
  go("admin-products");
  toast("Ürün kaydedildi.");
  return;
}
