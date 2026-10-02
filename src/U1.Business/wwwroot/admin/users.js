import {
  api,
  state,
  esc,
  icon,
  heading,
  empty,
  field,
  pager,
  modal,
  closeModal,
  toast,
  go,
} from "../app-core.js?v=20260928a";
import { adminSearch, selectField } from "./ui.js?v=20260928b";

let users = [];
let dealerGroups = [];

function showConflict(form, message, reload) {
  let notice = form.querySelector(".form-conflict");
  if (!notice) {
    notice = document.createElement("div");
    notice.className = "notice form-conflict";
    notice.setAttribute("role", "alert");
    form.append(notice);
  }
  notice.replaceChildren(document.createTextNode(message + " Girdiğiniz alanlar korunuyor. "));
  const button = document.createElement("button");
  button.type = "button";
  button.className = "button small";
  button.textContent = "Güncel bilgileri yükle";
  button.addEventListener("click", async () => {
    button.disabled = true;
    try {
      await reload();
    } catch (error) {
      toast(error.message, true);
    } finally {
      button.disabled = false;
    }
  });
  notice.append(button);
}

export async function userList() {
  const page = +(state.params.get("page") || 1);
  const [d, groupData] = await Promise.all([
    api("/admin/users?" + state.params),
    api("/admin/dealer-groups"),
  ]);
  users = d.items;
  dealerGroups = groupData.items;
  return (
    heading("Kullanıcılar", "Hesapları ve iletişim bilgilerini yönetin.") +
    /* HTML */ `<section class="panel">
      <form class="toolbar" data-form="admin-search">
        ${adminSearch("Ad, firma veya e-posta ara…")}
      </form>
      ${
        users.length
          ? /* HTML */ `<div class="table-scroll">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Ad soyad / Firma</th>
                    <th>E-posta</th>
                    <th>Telefon</th>
                    <th>Rol</th>
                    <th>Bayi grubu</th>
                    <th>Durum</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  ${users
                    .map(
                      (u) =>
                        /* HTML */ `<tr>
                          <td class="cell-stack">
                            <b>${esc(u.firstName)} ${esc(u.lastName)}</b
                            ><small>${esc(u.company || "—")}</small>
                          </td>
                          <td>${esc(u.email)}</td>
                          <td>${esc(u.phone)}</td>
                          <td>${u.role === "Admin" ? "Yönetici" : "Bayi"}</td>
                          <td>${u.role === "Dealer" ? esc(u.dealerGroupName || "—") : "—"}</td>
                          <td>
                            <span
                              class="status-badge ${u.isActive ? "approved" : "rejected"}"
                              >${u.isActive ? "Aktif" : "Pasif"}</span
                            >
                          </td>
                          <td>
                            <button
                              class="button small"
                              data-action="user-edit"
                              data-id="${u.id}"
                            >
                              ${icon("edit")} Düzenle
                            </button>
                          </td>
                        </tr>`,
                    )
                    .join("")}
                </tbody>
              </table>
            </div>`
          : empty("Kullanıcı bulunamadı.", "Arama kelimenizi değiştirin.")
      }${pager(d.total, page)}
    </section>` + dealerGroupPanel()
  );
}

function dealerGroupPanel() {
  return /* HTML */ `<section class="panel">
    <div class="section-title">
      <div><h2>Bayi grupları</h2><p class="muted">Her bayi grubuna tek yüzde iskonto uygulanır.</p></div>
    </div>
    <form class="toolbar" data-form="dealer-group-create">
      ${field("Yeni grup adı", "name", "", "text", 'required maxlength="80"')}
      ${field("İskonto (%)", "discountPercent", "0", "number", 'required min="0" max="100" step="0.01"')}
      <button class="button primary" type="submit">Grup ekle</button>
    </form>
    <div class="table-scroll">
      <table class="data-table">
        <thead><tr><th>Grup</th><th>İskonto</th><th>Bayi</th><th></th></tr></thead>
        <tbody>
          ${dealerGroups.map((group) => `
            <tr>
              <td colspan="4">
                <form class="toolbar" data-form="dealer-group-update" data-id="${group.id}" data-version="${esc(group.rowVersion)}">
                  ${field("Grup adı", "name", group.name, "text", 'required maxlength="80"')}
                  ${field("İskonto (%)", "discountPercent", group.discountPercent, "number", 'required min="0" max="100" step="0.01"')}
                  <span class="muted">${group.dealerCount} bayi</span>
                  <button class="button" type="submit">Kaydet</button>
                </form>
              </td>
            </tr>`).join("")}
        </tbody>
      </table>
    </div>
  </section>`;
}

export function showUserEditor(el) {
  const u = users.find((x) => x.id === +el.dataset.id);
  modal(
    "Kullanıcı bilgilerini düzenle",
    /* HTML */ `<form
      data-form="user"
      data-id="${u.id}"
      data-version="${u.version}"
    >
      <div class="form-grid">
        ${field("Ad", "firstName", u.firstName, "text", 'required maxlength="80"')}${field("Soyad", "lastName", u.lastName, "text", 'required maxlength="80"')}${field("E-posta", "email", u.email, "email", 'required maxlength="200"')}${field("Telefon", "phone", u.phone, "tel", 'required maxlength="25"')}
        <div class="full">
          ${field("Firma adı", "company", u.company, "text", 'maxlength="180"')}${field("Yeni şifre (isteğe bağlı)", "newPassword", "", "password", 'minlength="10" maxlength="128" autocomplete="new-password" placeholder="Değiştirmek istemiyorsanız boş bırakın"')}
        </div>
      </div>
        ${u.role === "Dealer"
          ? `<div class="full">${selectField(
              "Bayi grubu",
              "dealerGroupId",
              dealerGroups.map((group) => [group.id, `${group.name} · %${group.discountPercent}`]),
              u.dealerGroupId,
            )}</div>`
          : ""}
      <label class="check-row"
        ><input
          type="checkbox"
          name="isActive"
          ${u.isActive ? "checked" : ""}
          ${u.id === state.user.id ? "disabled" : ""}
        />Hesap aktif</label
      >
      <p class="muted">
        <small
          >Şifreler görüntülenmez. Yönetici hesabı ancak başka bir aktif yönetici
          varsa pasifleştirilebilir; kendi hesabınızı kapatamazsınız.
          Değişiklikten sonra kullanıcının yeniden giriş yapması gerekir.</small
        >
      </p>
      <div class="form-actions">
        <button class="button" type="button" data-action="close">Vazgeç</button
        ><button class="button primary" type="submit">Bilgileri kaydet</button>
      </div>
    </form>`,
  );
}

export async function submitUser(form, data, render) {
  data.isActive = form.querySelector("[name=isActive]").checked;
  data.version = +form.dataset.version;
  if (data.dealerGroupId) data.dealerGroupId = Number(data.dealerGroupId);
  try {
    await api("/admin/users/" + form.dataset.id, {
      method: "PUT",
      body: data,
    });
  } catch (error) {
    if (error.code === "USER_CHANGED") {
      showConflict(form, "Kullanıcı başka bir işlemde değişti.", async () => {
        const id = form.dataset.id;
        closeModal();
        await render();
        showUserEditor({ dataset: { id } });
      });
      return;
    }
    throw error;
  }
  closeModal();
  toast("Kullanıcı bilgileri kaydedildi.");
  if (+form.dataset.id === state.user.id) {
    state.user = null;
    go("login");
  } else await render();
  return;
}


export async function submitDealerGroupCreate(data, render) {
  data.discountPercent = Number(data.discountPercent);
  await api("/admin/dealer-groups", { method: "POST", body: data });
  toast("Bayi grubu oluşturuldu.");
  await render();
}

export async function submitDealerGroupUpdate(form, data, render) {
  data.discountPercent = Number(data.discountPercent);
  data.rowVersion = form.dataset.version;
  try {
    await api("/admin/dealer-groups/" + form.dataset.id, {
      method: "PUT",
      body: data,
    });
  } catch (error) {
    if (error.code === "DEALER_GROUP_CHANGED") {
      showConflict(form, "Bayi grubu başka bir işlemde değişti.", render);
      return;
    }
    throw error;
  }
  toast("Bayi grubu güncellendi.");
  await render();
}
