import {
  api,
  state,
  esc,
  icon,
  heading,
  field,
  modal,
  closeModal,
  toast,
} from "../app-core.js?v=20260928a";

let banners = [];

export async function bannerList() {
  banners = await api("/admin/banners");
  return (
    heading(
      "Duyurular",
      "Bayi ana sayfasındaki duyuru alanını yönetin.",
      /* HTML */ `<button
        class="button primary"
        data-action="banner-edit"
        data-id="0"
      >
        ${icon("plus")} Duyuru ekle
      </button>`,
    ) +
    /* HTML */ `<div class="banners-list">
      ${banners
        .map(
          (b) =>
            /* HTML */ `<article class="panel">
              <div class="banner-preview">
                <div class="eyebrow">DUYURU · ${b.position + 1}</div>
                <h3>${esc(b.title)}</h3>
                <p>${esc(b.subtitle)}</p>
              </div>
              <div class="banner-info">
                <span class="status-badge ${b.isActive ? "approved" : ""}"
                  >${b.isActive ? "Yayında" : "Pasif"}</span
                ><button
                  class="button small"
                  data-action="banner-edit"
                  data-id="${b.id}"
                >
                  ${icon("edit")} Düzenle
                </button>
              </div>
            </article>`,
        )
        .join("")}
    </div>`
  );
}

export function showBannerEditor(el) {
  const b = banners.find((x) => x.id === +el.dataset.id) || {
    title: "",
    subtitle: "",
    buttonText: "Ürünleri incele",
    searchTerm: "",
    isActive: true,
    position: banners.length,
  };
  modal(
    b.id ? "Duyuruyu düzenle" : "Yeni duyuru",
    /* HTML */ `<form data-form="banner" data-id="${b.id || ""}">
      ${b.id ? `<input type="hidden" name="rowVersion" value="${esc(b.rowVersion)}" />` : ""}
      ${field("Başlık", "title", b.title, "text", 'required maxlength="100"')}${field("Alt metin", "subtitle", b.subtitle, "text", 'required maxlength="300"')}
      <div class="form-grid">
        ${field("Buton metni", "buttonText", b.buttonText, "text", 'required maxlength="40"')}${field("Katalog arama kelimesi", "searchTerm", b.searchTerm, "text", 'maxlength="100"')}${field("Sıra", "position", b.position, "number", 'required min="0" max="100"')}
      </div>
      <label class="check-row"
        ><input
          type="checkbox"
          name="isActive"
          ${b.isActive ? "checked" : ""}
        />Yayında</label
      >
      <div class="form-actions">
        <button class="button" type="button" data-action="close">Vazgeç</button
        ><button class="button primary" type="submit">Kaydet</button>
      </div>
    </form>`,
  );
}

export async function submitBanner(form, data, render) {
  data.isActive = form.querySelector("[name=isActive]").checked;
  data.position = +data.position;
  try {
    await api("/admin/banners" + (form.dataset.id ? "/" + form.dataset.id : ""), {
      method: form.dataset.id ? "PUT" : "POST",
      body: data,
    });
  } catch (error) {
    if (error.code === "BANNER_CHANGED") {
      let notice = form.querySelector(".banner-conflict");
      if (!notice) {
        notice = document.createElement("div");
        notice.className = "notice banner-conflict";
        notice.setAttribute("role", "alert");
        form.prepend(notice);
      }
      notice.innerHTML =
        `Duyuru başka bir yönetici tarafından değiştirildi. Girdileriniz korunuyor. <button type="button" class="button small" data-action="reload-banner" data-id="${form.dataset.id}">Güncel duyuruyu yükle</button>.`;
      notice.querySelector("button").focus();
      return;
    }
    throw error;
  }
  state.meta = null;
  state.bannerId = null;
  closeModal();
  await render();
  toast("Duyuru kaydedildi.");
}

export async function reloadBannerEditor(el, render) {
  const id = +el.dataset.id;
  closeModal();
  await render();
  showBannerEditor({ dataset: { id: String(id) } });
}
