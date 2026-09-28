import {
  api,
  state,
  esc,
  icon,
  heading,
  toast,
} from "../app-core.js?v=20260928a";

let columns = [];

export async function grid() {
  columns = await api("/admin/grid");
  return (
    heading(
      "Katalog düzeni",
      "Bayi kataloğunun kolonlarını ve cihaz görünürlüğünü yapılandırın.",
    ) +
    /* HTML */ `<div class="notice">
        Sıra, genişlik ve görünüm değişiklikleri tüm bayilere uygulanır. Ürün
        adı, fiyat ve adet kolonları her cihazda açık kalır.
      </div>
      <form data-form="grid">
        <section class="panel">
          <div class="table-scroll">
            <table class="data-table grid-editor">
              <thead>
                <tr>
                  <th>Alan</th>
                  <th>Başlık</th>
                  <th>Görünüm</th>
                  <th>Sıra</th>
                  <th>Genişlik</th>
                  <th>Hizalama</th>
                  <th>Masaüstü</th>
                  <th>Tablet</th>
                  <th>Telefon</th>
                </tr>
              </thead>
              <tbody>
                ${columns
                  .map((c) => {
                    const opts =
                      c.field === "imageUrl"
                        ? ["image", "text"]
                        : c.field === "stock"
                          ? ["stock", "text"]
                          : c.field === "price"
                            ? ["money", "text"]
                            : c.field === "quantity"
                              ? ["purchase"]
                              : c.field === "name"
                                ? ["product", "text"]
                                : ["text"];
                    return /* HTML */ `<tr data-column="${c.id}">
                      <td class="mono">${c.field}</td>
                      <td>
                        <input
                          class="input"
                          name="label"
                          value="${esc(c.label)}"
                          required
                          maxlength="60"
                          aria-label="${esc(c.label)} başlığı"
                        />
                      </td>
                      <td>
                        <select
                          name="renderType"
                          class="select"
                          aria-label="${esc(c.label)} görünüm tipi"
                        >
                          ${opts.map((v) => /* HTML */ `<option ${c.renderType === v ? "selected" : ""} value="${v}">${{ image: "Görsel", text: "Metin", stock: "Stok göstergesi", money: "Para birimi", purchase: "Adet / Sepet", product: "Ürün detayı" }[v]}</option>`).join("")}
                        </select>
                      </td>
                      <td>
                        <input
                          class="input"
                          name="position"
                          type="number"
                          min="0"
                          max="100"
                          value="${c.position}"
                          required
                          aria-label="${esc(c.label)} sırası"
                        />
                      </td>
                      <td>
                        <input
                          class="input"
                          name="width"
                          type="number"
                          min="60"
                          max="600"
                          value="${c.width}"
                          required
                          aria-label="${esc(c.label)} genişliği"
                        />
                      </td>
                      <td>
                        <select
                          name="align"
                          class="select"
                          aria-label="${esc(c.label)} hizalama"
                        >
                          ${[
                            ["left", "Sol"],
                            ["center", "Orta"],
                            ["right", "Sağ"],
                          ]
                            .map(
                              ([v, l]) =>
                                /* HTML */ `<option
                                  value="${v}"
                                  ${c.align === v ? "selected" : ""}
                                >
                                  ${l}
                                </option>`,
                            )
                            .join("")}
                        </select>
                      </td>
                      ${["desktop", "tablet", "mobile"].map((device) => /* HTML */ `<td><input type="checkbox" name="${device}" ${c[device] ? "checked" : ""} ${["name", "price", "quantity"].includes(c.field) ? "disabled" : ""} aria-label="${esc(c.label)} ${device}" /></td>`).join("")}
                    </tr>`;
                  })
                  .join("")}
              </tbody>
            </table>
          </div>
        </section>
        <div class="form-actions">
          <a class="button" href="#catalog">Kataloğu görüntüle</a
          ><button class="button primary" type="submit">
            ${icon("check")} Düzeni kaydet
          </button>
        </div>
      </form>`
  );
}

export async function submitGrid(form, render) {
  const data = columns.map((c) => {
    const row = form.querySelector(`[data-column="${c.id}"]`),
      get = (n) => row.querySelector(`[name="${n}"]`);
    return {
      ...c,
      label: get("label").value,
      renderType: get("renderType").value,
      position: +get("position").value,
      width: +get("width").value,
      align: get("align").value,
      desktop: get("desktop").checked,
      tablet: get("tablet").checked,
      mobile: get("mobile").checked,
    };
  });
  await api("/admin/grid", { method: "PUT", body: data });
  state.meta = null;
  await render();
  toast("Katalog düzeni kaydedildi.");
  return;
}
