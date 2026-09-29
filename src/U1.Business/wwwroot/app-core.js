const ICON_PATHS = {
    home: 'M3 10 12 3l9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z',
    grid: 'M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z',
    cart: 'M2 3h3l3 12h10l3-9H6 M9 20h.01 M18 20h.01 M12 8v5m-2-2h4',
    box: 'm3 7 9-4 9 4v10l-9 4-9-4Z M3 7l9 4 9-4 M12 11v10 M7 5l10 4',
    search: 'm21 21-5-5 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0',
    arrow: 'M4 12h16m-6-6 6 6-6 6',
    chevron: 'm9 5 7 7-7 7',
    logout: 'M9 4H4v16h5 M11 12h10m-4-4 4 4-4 4',
    users: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2 M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8 M17 4a4 4 0 0 1 0 7 M20 21v-2a4 4 0 0 0-3-4',
    chart: 'M4 3v18h17 M8 16v-5m5 5V7m5 9v-8',
    sliders: 'M4 7h5m4 0h7 M4 17h11m4 0h1 M9 4v6 M15 14v6',
    banner: 'M3 4h18v16H3z M3 14l5-5 5 6 3-3 5 6 M16 8h.01',
    plus: 'M12 5v14M5 12h14',
    close: 'm6 6 12 12M6 18 18 6',
    check: 'm5 12 4 4L19 6',
    trash: 'M3 6h18 M9 6V3h6v3 M5 6l1 15h12l1-15 M10 10v7m4-7v7',
    edit: 'm15 4 5 5 M3 21l5-1L21 7l-5-5L3 15Z',
    menu: 'M4 6h16M4 12h16M4 18h16',
    shield: 'm12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6Z m-4 9 3 3 5-6',
    download: 'M12 3v12m-5-5 5 5 5-5 M4 17v4h16v-4',
    refresh: 'M20 11a8 8 0 1 0-2 7 M20 4v7h-7',
    mail: 'M3 5h18v14H3z M3 5l9 7 9-7',
    more: 'M5 12h.01M12 12h.01M19 12h.01',
    down: 'm6 9 6 6 6-6',
    card: 'M3 5h18v14H3z M3 10h18 M7 15h3',
    bank: 'm3 8 9-5 9 5H3 M5 11v7m7-7v7m7-7v7M3 21h18',
    help: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20 M9 8a3 3 0 0 1 6 0c0 2-3 2-3 5 M12 17h.01',
};

export const state = {
    user: null,
    meta: null,
    cart: { items: [], total: 0, count: 0 },
    route: 'home',
    params: new URLSearchParams(),
    filters: { q: '', category: '', brand: '', stock: '', sort: '', page: 1 },
    bannerId: null,
    products: [],
};

let csrfToken = '';
let unauthorizedHandler = () => {};
let sameRouteHandler = () => {};
let modalReturnFocus = null;
let pendingModalReturnFocus = null;

export function setUnauthorizedHandler(handler) {
    unauthorizedHandler = handler;
}

export function resetCsrf() {
    csrfToken = '';
}

export function setSameRouteHandler(handler) {
    sameRouteHandler = handler;
}

function normalizeKeys(value) {
    if (Array.isArray(value)) {
        return value.map(normalizeKeys);
    }

    if (value && typeof value === 'object') {
        return Object.fromEntries(
            Object.entries(value).map(([key, item]) => [
                key[0].toLowerCase() + key.slice(1),
                normalizeKeys(item),
            ]),
        );
    }

    return value;
}

async function fetchSafe(...args) {
    try {
        return await fetch(...args);
    } catch {
        throw new Error('Sunucuya ulaşılamadı. Bağlantınızı kontrol edip tekrar deneyin.');
    }
}

export async function api(path, options = {}) {
    const request = { ...options };
    const headers = { ...options.headers };

    if (options.body && !(options.body instanceof FormData)) {
        headers['Content-Type'] = 'application/json';
        request.body = JSON.stringify(options.body);
    }

    if (options.method && options.method !== 'GET') {
        if (!csrfToken) {
            const csrfResponse = await fetchSafe('/api/csrf');
            csrfToken = (await csrfResponse.json()).token;
        }
        headers['X-CSRF-TOKEN'] = csrfToken;
    }

    const response = await fetchSafe('/api' + path, { ...request, headers });
    const responseText = await response.text();

    let data = null;
    try {
        data = responseText ? normalizeKeys(JSON.parse(responseText)) : null;
    } catch {
        data = null;
    }

    if (response.ok) {
        return data;
    }

    if (response.status === 401 && !path.startsWith('/auth/')) {
        unauthorizedHandler();
        state.user = null;
        location.hash = 'login';
    }

    const fallbackMessage =
        response.status === 429
            ? 'Çok fazla deneme yaptınız. Bir dakika sonra tekrar deneyin.'
            : response.status === 403
                ? 'Bu işlem için yetkiniz yok.'
                : 'İşlem tamamlanamadı. Tekrar deneyin.';

    const error = new Error(data?.message || fallbackMessage);
    error.status = response.status;
    error.code = data?.code;
    throw error;
}

export function icon(name) {
    const path = ICON_PATHS[name] || ICON_PATHS.box;
    return `<svg class="icon" aria-hidden="true" viewBox="0 0 24 24"><path d="${path}"/></svg>`;
}

export function esc(value) {
    return String(value ?? '').replace(
        /[&<>"']/g,
        char => ({
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#39;',
        })[char],
    );
}

export function money(value) {
    return new Intl.NumberFormat('tr-TR', {
        style: 'currency',
        currency: 'TRY',
        minimumFractionDigits: 2,
    }).format(value || 0);
}

export function date(value) {
    const normalized = value.endsWith('Z') ? value : value + 'Z';
    return new Date(normalized).toLocaleDateString('tr-TR', {
        day: '2-digit',
        month: 'short',
        year: 'numeric',
    });
}

export function go(route) {
    if (location.hash === '#' + route) {
        sameRouteHandler();
        return;
    }

    location.hash = route;
}

export function toast(text, error = false) {
    const element = document.createElement('div');
    element.className = 'toast' + (error ? ' error' : '');
    element.textContent = text;
    document.querySelector('#toasts').append(element);
    setTimeout(() => element.remove(), 4500);
}

export function brand() {
    return `<a class="brand" href="#home" aria-label="U1 Business ana sayfa">
        <img class="brand-mark" src="/images/brand.svg" width="42" height="42" alt="">
        <span class="brand-name">u1<span class="wordmark-weight">business</span><small>BAYİ PORTALI</small></span>
    </a>`;
}

export function status(value) {
    const className =
        value === 'Onaylandı'
            ? 'approved'
            : value === 'Reddedildi'
                ? 'rejected'
                : 'pending';

    return `<span class="status-badge ${className}">${esc(value)}</span>`;
}

export function stock(product) {
    const className =
        product.stock === 0
            ? 'empty'
            : product.stock <= product.criticalStock
                ? 'critical'
                : '';

    const label =
        product.stock === 0
            ? 'Yok'
            : product.stock <= product.criticalStock
                ? 'Kritik'
                : 'Var';

    return `<span class="stock ${className}">${label}</span>`;
}

export function thumb(product) {
    const source = product.imageUrl || '/images/product.svg';
    return `<img class="product-thumb" src="${esc(source)}" alt="${esc(product.name)}" loading="lazy">`;
}

export function heading(title, description, actions = '') {
    const eyebrow = state.route.startsWith('admin') ? 'YÖNETİM MERKEZİ' : 'BAYİ PORTALI';

    return `<div class="pagehead">
        <div>
            <div class="eyebrow">${eyebrow}</div>
            <h1>${title}</h1>
            ${description ? `<p>${description}</p>` : ''}
        </div>
        <div class="heading-actions">${actions}</div>
    </div>`;
}

export function empty(title, description, action = '') {
    return `<div class="empty-state">
        ${icon('box')}
        <h3>${title}</h3>
        <p>${description}</p>
        ${action}
    </div>`;
}

export function field(label, name, value = '', type = 'text', attrs = '') {
    if (name === 'stock') {
        attrs = attrs.replace('max="1000000"', 'max="2147483647"');
    }

    return `<div class="field">
        <label for="field-${name}">${label}</label>
        <input class="input" id="field-${name}" name="${name}" type="${type}" value="${esc(value)}" ${attrs}>
    </div>`;
}

export function pager(total, page = 1) {
    const range = total
        ? `${(page - 1) * 20 + 1}–${Math.min(page * 20, total)} / ${total} kayıt`
        : '0 kayıt';

    return `<div class="table-footer">
        <span>${range}</span>
        <div class="pages">
            <button data-action="page" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''} aria-label="Önceki sayfa">‹</button>
            <span>${page}</span>
            <button data-action="page" data-page="${page + 1}" ${page * 20 >= total ? 'disabled' : ''} aria-label="Sonraki sayfa">›</button>
        </div>
    </div>`;
}

export function formData(form) {
    return Object.fromEntries(new FormData(form));
}

export function rememberModalTrigger(element) {
    pendingModalReturnFocus =
        element instanceof HTMLElement && element.isConnected
            ? element
            : null;
}

export function modal(title, body) {
    const dialog = document.querySelector('#dialog');
    dialog.innerHTML = `<div class="dialog-head">
        <h2 id="dialog-title">${esc(title)}</h2>
        <button class="icon-button" data-action="close" aria-label="Pencereyi kapat">${icon('close')}</button>
    </div>
    <div class="dialog-body">${body}</div>`;

    if (!dialog.open) {
        modalReturnFocus =
            pendingModalReturnFocus ||
            (document.activeElement instanceof HTMLElement
                ? document.activeElement
                : null);
        pendingModalReturnFocus = null;
        dialog.showModal();
    }
}

export function closeModal() {
    document.querySelector('#dialog').close();
}

export function restoreModalFocus() {
    const target = modalReturnFocus;
    modalReturnFocus = null;

    if (target?.isConnected) {
        setTimeout(() => {
            if (target.isConnected) {
                target.focus({ preventScroll: true });
            }
        }, 0);
    }
}

export async function refreshCart() {
    state.cart = await api('/cart');

    document.querySelectorAll('[data-cart-count]').forEach(element => {
        element.textContent = state.cart.count;
    });

    document.querySelectorAll('[data-cart-total]').forEach(element => {
        element.textContent = money(state.cart.total);
    });
}
