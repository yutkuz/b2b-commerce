import {
  api,
  esc,
  heading,
  state,
  toast,
} from "../app-core.js?v=20260928a";

function targetOptions(categories, sourceId) {
  return categories
    .filter((category) => category.id !== sourceId)
    .map(
      (category) =>
        `<option value="${category.id}|${esc(category.rowVersion)}">${esc(category.name)}</option>`,
    )
    .join("");
}

export async function categoryList() {
  const categories = await api("/admin/categories");

  return (
    heading(
      "Kategori yönetimi",
      "Kategorileri ekleyin, yeniden adlandırın veya ürünleri hedef kategoriye taşıyarak birleştirin.",
    ) +
    `<section class="panel">
      <div class="section-title"><h2>Yeni kategori</h2></div>
      <form class="toolbar" data-form="category-create">
        <div class="field" style="margin:0;min-width:260px">
          <label for="new-category-name">Kategori adı</label>
          <input class="input" id="new-category-name" name="name" maxlength="80" required />
        </div>
        <button class="button primary" type="submit">Kategori ekle</button>
      </form>
    </section>
    <section class="panel" style="margin-top:22px">
      <div class="section-title">
        <h2>Mevcut kategoriler</h2>
        <small class="muted">Birleştirme kaynak kategoriyi kaldırır ve tüm ürünlerini hedef kategoriye taşır.</small>
      </div>
      ${
        categories.length
          ? `<div class="table-scroll">
              <table class="data-table">
                <thead>
                  <tr><th>Kategori</th><th>Ürün</th><th>Arşivde</th><th>Yeniden adlandır</th><th>Birleştir</th></tr>
                </thead>
                <tbody>
                  ${categories
                    .map(
                      (category) => `<tr>
                        <td class="cell-stack"><b>${esc(category.name)}</b><small>#${category.id}</small></td>
                        <td>${category.productCount}</td>
                        <td>${category.archivedProductCount}</td>
                        <td>
                          <form class="toolbar" data-form="category-update" data-id="${category.id}" data-version="${esc(category.rowVersion)}">
                            <input class="input" name="name" value="${esc(category.name)}" maxlength="80" required aria-label="${esc(category.name)} kategori adı" />
                            <button class="button small" type="submit">Kaydet</button>
                          </form>
                        </td>
                        <td>
                          <form class="toolbar" data-form="category-merge" data-id="${category.id}" data-version="${esc(category.rowVersion)}">
                            <select class="input" name="target" aria-label="${esc(category.name)} hedef kategori" ${categories.length < 2 ? "disabled" : ""}>
                              ${targetOptions(categories, category.id)}
                            </select>
                            <button class="button small" type="submit" ${categories.length < 2 ? "disabled" : ""}>Birleştir</button>
                          </form>
                        </td>
                      </tr>`,
                    )
                    .join("")}
                </tbody>
              </table>
            </div>`
          : `<div class="empty-state"><h3>Kategori bulunamadı.</h3></div>`
      }
    </section>`
  );
}

export async function submitCategoryCreate(data, render) {
  await api("/admin/categories", { method: "POST", body: data });
  state.meta = null;
  await render();
  toast("Kategori eklendi.");
}

export async function submitCategoryUpdate(form, data, render) {
  await api("/admin/categories/" + form.dataset.id, {
    method: "PUT",
    body: { name: data.name, rowVersion: form.dataset.version },
  });
  state.meta = null;
  await render();
  toast("Kategori güncellendi.");
}

export async function submitCategoryMerge(form, data, render) {
  const [targetCategoryId, targetRowVersion] = String(data.target || "").split("|");
  if (!targetCategoryId || !targetRowVersion) throw new Error("Hedef kategori seçin.");

  await api("/admin/categories/" + form.dataset.id + "/merge", {
    method: "POST",
    body: {
      targetCategoryId: Number(targetCategoryId),
      sourceRowVersion: form.dataset.version,
      targetRowVersion,
    },
  });
  state.meta = null;
  await render();
  toast("Kategoriler birleştirildi.");
}
