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
import { adminSearch } from "./ui.js?v=20260928b";

let users = [];

export async function userList() {
  const page = +(state.params.get("page") || 1);
  const d = await api("/admin/users?" + state.params);
  users = d.items;
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
    </section>`
  );
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
  try {
    await api("/admin/users/" + form.dataset.id, {
      method: "PUT",
      body: data,
    });
  } catch (error) {
    if (error.code === "USER_CHANGED") {
      toast(
        "Kullanıcı başka bir işlemde değişti. Güncel bilgileri yeniden açın.",
      );
      closeModal();
      await render();
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
