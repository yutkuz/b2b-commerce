import { go, state } from "./app-core.js?v=20260928a";
import { dashboard } from "./admin/dashboard.js?v=20260928b";
import {
  products,
  productForm,
  submitProduct,
} from "./admin/products.js?v=20260928b";
import {
  userList,
  showUserEditor,
  submitUser,
} from "./admin/users.js?v=20260928b";
import { orderList } from "./admin/orders.js?v=20260928b";
import { grid, submitGrid } from "./admin/grid.js?v=20260928b";
import {
  bannerList,
  showBannerEditor,
  submitBanner,
  reloadBannerEditor,
} from "./admin/banners.js?v=20260928b";

export async function renderAdmin() {
  switch (state.route) {
    case "admin":
      return dashboard();
    case "admin-products":
      return products();
    case "admin-product":
      return productForm();
    case "admin-users":
      return userList();
    case "admin-orders":
      return orderList();
    case "admin-grid":
      return grid();
    case "admin-banners":
      return bannerList();
  }
}

export async function adminAction(action, element, { render }) {
  if (action === "reload-product") return render();
  if (action === "reload-grid") return render();
  if (action === "reload-banner") return reloadBannerEditor(element, render);
  if (action === "user-edit") return showUserEditor(element);
  if (action === "banner-edit") return showBannerEditor(element);
}

export async function adminSubmit(kind, form, data, { render }) {
  if (kind === "admin-search") {
    const query = new URLSearchParams(data);
    query.set("page", "1");
    go(state.route + "?" + query);
    return;
  }
  if (kind === "product") return submitProduct(form, data);
  if (kind === "user") return submitUser(form, data, render);
  if (kind === "grid") return submitGrid(form, render);
  if (kind === "banner") return submitBanner(form, data, render);
}
