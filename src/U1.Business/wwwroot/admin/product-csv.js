import {
  api,
  closeModal,
  empty,
  esc,
  modal,
  toast,
} from "../app-core.js?v=20260928a";

let pendingImport = null;

const fields = [
  ["name", "Ad"],
  ["description", "Açıklama"],
  ["brand", "Marka"],
  ["manufacturerCode", "Üretici kodu"],
  ["specialCode1", "Özel kod 1"],
  ["specialCode2", "Özel kod 2"],
  ["imageUrl", "Görsel"],
  ["stock", "Stok"],
  ["criticalStock", "Kritik stok"],
  ["price", "Fiyat"],
  ["category", "Kategori"],
];

export function openCsvImport() {
  pendingImport = null;
  modal(
    "Ürünleri CSV ile aktar",
    /* HTML */ `<form data-form="csv-preview">
      <p class="muted">Belgelenen 14 sütunlu CSV şablonunu kullanın. En fazla 1 MiB ve 1000 ürün satırı seçebilirsiniz.</p>
      <div class="field">
        <label for="product-csv-file">CSV dosyası</label>
        <input id="product-csv-file" name="csvFile" class="input" type="file" accept=".csv,text/csv" required />
      </div>
      <div class="form-actions">
        <button class="button" type="button" data-action="close">Vazgeç</button>
        <button class="button primary" type="submit">Değişiklikleri önizle</button>
      </div>
    </form>`,
  );
}

export async function previewCsvImport(form) {
  const file = form.querySelector('[name="csvFile"]').files[0];
  if (!file) throw new Error("Bir CSV dosyası seçin.");
  if (file.size > 1048576) throw new Error("CSV en fazla 1 MiB olabilir.");

  const csv = await file.text();
  const preview = await api("/admin/products/import/preview", {
    method: "POST",
    body: { csv },
  });
  pendingImport = preview.valid
    ? { csv, previewToken: preview.previewToken, importId: crypto.randomUUID() }
    : null;

  const errorRows = preview.errors.length
    ? preview.errors.map((error) => /* HTML */ `<li>
        Satır ${error.row}: ${esc(error.message)}
      </li>`).join("")
    : "";
  const changes = preview.changes.length
    ? preview.changes.map(renderChange).join("")
    : empty("Uygulanacak satır yok.", "CSV dosyasını kontrol edin.");

  modal(
    "CSV değişiklik önizlemesi",
    /* HTML */ `<p class="muted">
      ${preview.rows} satır · ${preview.creates} yeni ürün · ${preview.updates} güncelleme
    </p>
    ${errorRows ? `<div class="notice error"><b>Önce hataları düzeltin:</b><ul>${errorRows}</ul></div>` : ""}
    <div class="table-scroll" style="max-height:45vh;overflow:auto">${changes}</div>
    <div class="form-actions">
      <button class="button" type="button" data-action="csv-import-open">Başka dosya seç</button>
      ${preview.valid ? `<button class="button primary" type="button" data-action="csv-import-apply">Önizlenen değişiklikleri uygula</button>` : ""}
    </div>`,
  );
}

function renderChange(change) {
  const differences = fields
    .filter(([key]) => change.before === null
      || String(change.before[key] ?? "") !== String(change.after[key] ?? ""))
    .map(([key, label]) => /* HTML */ `<li>
      <b>${label}:</b>
      ${change.before === null ? "Yeni" : esc(change.before[key] ?? "—")}
      → ${esc(change.after[key] ?? "—")}
    </li>`)
    .join("");
  return /* HTML */ `<section class="panel" style="margin:8px 0">
    <b>Satır ${change.row} · ${esc(change.code)} · ${change.action === "create" ? "Yeni" : "Güncelleme"}</b>
    ${differences ? `<ul>${differences}</ul>` : `<p class="muted">Alanlarda değişiklik yok.</p>`}
  </section>`;
}

export async function applyCsvImport(render) {
  if (!pendingImport) throw new Error("Önce CSV dosyasını önizleyin.");
  const result = await api("/admin/products/import/apply", {
    method: "POST",
    body: pendingImport,
  });
  pendingImport = null;
  closeModal();
  await render();
  toast(result.alreadyApplied
    ? "Bu CSV aktarımı daha önce uygulanmıştı."
    : `${result.applied} ürün satırı uygulandı.`);
}
