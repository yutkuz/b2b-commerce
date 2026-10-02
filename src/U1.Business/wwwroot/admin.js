import { go, state } from "./app-core.js?v=20260928a";
import { dashboard } from "./admin/dashboard.js?v=20261001e";
import {
  products,
  productForm,
  submitProduct,
  archiveProduct,
  restoreProduct,
  previewImageCleanup,
  cleanupImages,
} from "./admin/products.js?v=20261001e";
import {
  openCsvImport,
  previewCsvImport,
  applyCsvImport,
} from "./admin/product-csv.js?v=20261001d";
import {
  categoryList,
  submitCategoryCreate,
  submitCategoryUpdate,
  submitCategoryMerge,
} from "./admin/categories.js?v=20260929b";
import { history } from "./admin/history.js?v=20261001e";
import {
  userList,
  showUserEditor,
  submitUser,
  submitDealerGroupCreate,
  submitDealerGroupUpdate,
} from "./admin/users.js?v=20261001e";
import { orderList } from "./admin/orders.js?v=20260930a";
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
    case "admin-categories":
      return categoryList();
    case "admin-users":
      return userList();
    case "admin-orders":
      return orderList();
    case "admin-grid":
      return grid();
    case "admin-banners":
      return bannerList();
    case "admin-history":
      return history();
  }
}

export async function adminAction(action, element, { render }) {
  if (action === "reload-product") return render();
  if (action === "archive-product") return archiveProduct(element, render);
  if (action === "restore-product") return restoreProduct(element, render);
  if (action === "preview-image-cleanup") return previewImageCleanup();
  if (action === "cleanup-images") return cleanupImages(element, render);
  if (action === "csv-import-open") return openCsvImport();
  if (action === "csv-import-apply") return applyCsvImport(render);
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
  if (kind === "admin-history-filter") {
    const query = new URLSearchParams(
      Object.entries(data).filter(([, item]) => item !== ""),
    );
    query.set("page", "1");
    go("admin-history?" + query);
    return;
  }
  if (kind === "product") return submitProduct(form, data);
  if (kind === "csv-preview") return previewCsvImport(form);
  if (kind === "category-create") return submitCategoryCreate(data, render);
  if (kind === "category-update") return submitCategoryUpdate(form, data, render);
  if (kind === "category-merge") return submitCategoryMerge(form, data, render);
  if (kind === "user") return submitUser(form, data, render);
  if (kind === "dealer-group-create") return submitDealerGroupCreate(data, render);
  if (kind === "dealer-group-update") return submitDealerGroupUpdate(form, data, render);
  if (kind === "grid") return submitGrid(form, render);
  if (kind === "banner") return submitBanner(form, data, render);
}
