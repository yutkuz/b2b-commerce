import { renderAdmin } from './admin.js?v=20261001a';
import {
    portalRoutes,
    publicRoutes,
    portalShell,
    portalFooter,
    publicShell,
    renderPortalPage,
} from './portal.js?v=20260930a';
import {
    api,
    brand,
    closeModal,
    esc,
    go,
    refreshCart,
    setSameRouteHandler,
    setUnauthorizedHandler,
    state,
} from './app-core.js?v=20260928a';
import { renderAuth } from './auth-view.js?v=20260928a';
import { renderCatalogPage } from './catalog.js?v=20260928a';
import {
    clearCheckoutSession,
    renderCartPage,
} from './checkout.js?v=20260929b';
import { bindAppEvents } from './app-events.js?v=20260930d';
import { renderOrdersPage } from './orders.js?v=20260930a';

const ROUTES = {
    home: ['Ana sayfa', 'home'],
    catalog: ['Arama', 'grid'],
    cart: ['Sepetim', 'cart'],
    orders: ['Siparişlerim', 'box'],
    admin: ['Genel bakış', 'chart'],
    'admin-products': ['Ürün yönetimi', 'grid'],
    'admin-product': ['Ürün bilgileri', 'grid'],
    'admin-categories': ['Kategori yönetimi', 'grid'],
    'admin-users': ['Kullanıcılar', 'users'],
    'admin-orders': ['Sipariş yönetimi', 'box'],
    'admin-grid': ['Katalog düzeni', 'sliders'],
    'admin-banners': ['Duyurular', 'banner'],
    'admin-history': ['İşlem geçmişi', 'chart'],
    ...portalRoutes,
};

const CATALOG_ROUTES = new Set(['home', 'catalog', 'new-products']);

let renderVersion = 0;

function resetFilters() {
    state.filters = {
        q: '',
        category: '',
        brand: '',
        stock: '',
        sort: '',
        page: 1,
    };
}

function parseRoute() {
    const [route, query = ''] = (location.hash.slice(1) || 'home').split('?');

    state.route = route;
    state.params = new URLSearchParams(query);

    return route;
}

function renderShell() {
    return portalShell(ROUTES, brand);
}

function pageTitle(route) {
    if (publicRoutes.includes(route)) {
        return ROUTES[route][0];
    }

    if (route === 'register') {
        return 'Bayi kaydı';
    }

    if (route === 'login') {
        return 'Bayi girişi';
    }

    return ROUTES[route]?.[0] || 'U1 Business';
}

function restoreFocusedElement(focusId, selectionStart) {
    if (!focusId) {
        return;
    }

    const element = document.getElementById(focusId);
    if (!element) {
        return;
    }

    element.focus({ preventScroll: true });

    try {
        element.setSelectionRange(selectionStart, selectionStart);
    } catch {
        // Not every focusable element supports text selection.
    }
}

async function renderAuthenticatedPage(route) {
    if (!state.meta || CATALOG_ROUTES.has(route)) {
        state.meta = await api('/catalog/meta');
    }

    if (route.startsWith('admin')) {
        return renderAdmin();
    }

    if (route === 'cart') {
        return renderCartPage();
    }

    if (route === 'orders') {
        return renderOrdersPage();
    }

    if (portalRoutes[route] && route !== 'new-products') {
        return renderPortalPage();
    }

    return renderCatalogPage();
}

export async function render() {
    const version = ++renderVersion;
    const previousRoute = state.route;
    const route = parseRoute();

    if (route === 'new-products' && previousRoute !== route) {
        resetFilters();
    }

    if (!state.user) {
        const isPublicRoute = publicRoutes.includes(route);
        if (!isPublicRoute && route !== 'login' && route !== 'register') {
            go('login');
            return;
        }

        document.querySelector('#app').innerHTML = isPublicRoute
            ? publicShell(brand)
            : renderAuth(route === 'register') + portalFooter();

        document.title = `${pageTitle(route)} · U1 Business`;
        return;
    }

    if (route === 'login' || route === 'register') {
        go('home');
        return;
    }

    if (route.startsWith('admin') && state.user.role !== 'Admin') {
        go('home');
        return;
    }

    if (!ROUTES[route]) {
        go('home');
        return;
    }

    const shellRoute = document.querySelector('[data-shell-route]')?.dataset.shellRoute;
    if (!document.querySelector('#page') || shellRoute !== route) {
        document.querySelector('#app').innerHTML = renderShell();
    }

    const page = document.querySelector('#page');
    page.setAttribute('aria-busy', 'true');

    try {
        const html = await renderAuthenticatedPage(route);

        if (version !== renderVersion) {
            return;
        }

        const activeElement = document.activeElement;
        const focusId = activeElement?.id;
        const selectionStart = activeElement?.selectionStart;

        page.innerHTML = html;
        page.removeAttribute('aria-busy');
        document.title = `${ROUTES[route][0]} · U1 Business`;

        restoreFocusedElement(focusId, selectionStart);
    } catch (error) {
        if (version !== renderVersion) {
            return;
        }

        page.innerHTML = `<div class="content-error">
            <h2>Bu ekran yüklenemedi.</h2>
            <p>${esc(error.message)}</p>
            <button class="button" data-action="retry">Tekrar dene</button>
        </div>`;
        page.removeAttribute('aria-busy');
    }
}

async function boot() {
    try {
        state.user = await api('/auth/me');
        await refreshCart();
    } catch {
        state.user = null;
    }

    await render();
}

setSameRouteHandler(render);
setUnauthorizedHandler(clearCheckoutSession);

bindAppEvents({
    render,
    resetFilters,
    catalogRouteNames: CATALOG_ROUTES,
});

window.addEventListener('hashchange', () => {
    closeModal();
    render();
    window.scrollTo({ top: 0 });
});

window.addEventListener('focus', async () => {
    if (state.user && ['orders', 'admin-orders'].includes(state.route)) {
        await render();
    }
});

boot();
