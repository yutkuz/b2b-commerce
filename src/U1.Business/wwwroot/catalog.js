import {
    api,
    empty,
    esc,
    heading,
    icon,
    modal,
    money,
    pager,
    state,
    stock,
    thumb,
} from './app-core.js?v=20260928a';

const SORT_OPTIONS = [
    ['', 'Varsayılan sıralama'],
    ['name', 'Ürün adına göre'],
    ['price-asc', 'Fiyat: düşükten yükseğe'],
    ['price-desc', 'Fiyat: yüksekten düşüğe'],
    ['newest', 'Son eklenenler'],
];

export function selectedBanner() {
    const items = state.meta?.banners || [];
    const selectedIndex = Math.max(0, items.findIndex(item => item.id === state.bannerId));

    state.bannerId = items[selectedIndex]?.id ?? null;

    return {
        items,
        selectedIndex,
        banner: items[selectedIndex],
    };
}

export function renderBanner() {
    const { items, selectedIndex, banner } = selectedBanner();

    if (!banner) {
        return '';
    }

    const controls = items.map((_, index) => {
        const isActive = index === selectedIndex;
        return `<button
            class="${isActive ? 'active' : ''}"
            data-action="banner-slide"
            data-index="${index}"
            aria-label="${index + 1}. duyuru"
            aria-pressed="${isActive}">
            ${String(index + 1).padStart(2, '0')}
        </button>`;
    }).join('');

    return `<section class="banner bulletin" aria-label="Duyurular">
        <div class="bulletin-label">
            <span>DUYURULAR</span>
            <b>${String(selectedIndex + 1).padStart(2, '0')}<small> / ${String(items.length).padStart(2, '0')}</small></b>
        </div>
        <div class="bulletin-copy" aria-live="polite">
            <h2>${esc(banner.title)}</h2>
            <p>${esc(banner.subtitle)}</p>
        </div>
        <button class="button bulletin-link" data-action="banner-go">
            ${esc(banner.buttonText)} ${icon('arrow')}
        </button>
        <div class="banner-controls">${controls}</div>
    </section>`;
}

function renderFilters() {
    const filters = state.filters;
    const totalProducts = state.meta.categories.reduce(
        (total, category) => total + category.productCount,
        0,
    );

    const categories = state.meta.categories.map(category => `
        <button
            class="category-button ${+filters.category === category.id ? 'active' : ''}"
            data-action="category"
            data-id="${category.id}">
            ${esc(category.name)}
            <small>${category.productCount}</small>
        </button>
    `).join('');

    const brands = state.meta.brands.map(brand => `
        <label class="check-label">
            <input
                type="radio"
                name="brand"
                data-filter="brand"
                value="${esc(brand)}"
                ${filters.brand === brand ? 'checked' : ''}>
            ${esc(brand)}
        </label>
    `).join('');

    return `<aside class="filters" aria-label="Ürün filtreleri">
        <div class="filter-title">
            <h3>Filtreler</h3>
            <button class="text-button" data-action="clear-filters">Temizle</button>
        </div>

        <div class="filter-group">
            <h3>Kategoriler</h3>
            <div class="category-list">
                <button
                    class="category-button ${!filters.category ? 'active' : ''}"
                    data-action="category"
                    data-id="">
                    Tüm ürünler
                    <small>${totalProducts}</small>
                </button>
                ${categories}
            </div>
        </div>

        <div class="filter-group brands-filter">
            <h3>Marka</h3>
            ${brands}
            <label class="check-label">
                <input
                    type="radio"
                    name="brand"
                    data-filter="brand"
                    value=""
                    ${!filters.brand ? 'checked' : ''}>
                Tüm markalar
            </label>
        </div>

        <div class="filter-group stock-filter">
            <h3>Stok durumu</h3>
            <label class="check-label">
                <input type="checkbox" data-filter="stock" value="available" ${filters.stock === 'available' ? 'checked' : ''}>
                Yalnızca stoktakiler
            </label>
            <label class="check-label">
                <input type="checkbox" data-filter="stock" value="critical" ${filters.stock === 'critical' ? 'checked' : ''}>
                Kritik stok
            </label>
        </div>

        <div class="filter-help">
            <strong>Aradığınızı hemen bulun.</strong><br>
            Ürün adı, marka, ürün kodu veya üretici koduyla arayabilirsiniz.
        </div>
    </aside>`;
}

export function quantityControl(product, context = 'catalog') {
    const maxQuantity = Math.min(product.stock, 1000000);
    const disabled = product.stock === 0 ? 'disabled' : '';

    return `<div class="quantity">
        <button data-action="quantity" data-delta="-1" aria-label="Adedi azalt" ${disabled}>−</button>
        <input
            type="number"
            min="1"
            max="${maxQuantity}"
            step="1"
            value="${product.quantity || 1}"
            aria-label="${esc(product.name)} adet"
            data-quantity="${product.id}"
            data-context="${context}"
            ${disabled}>
        <button data-action="quantity" data-delta="1" aria-label="Adedi artır" ${disabled}>+</button>
    </div>`;
}

const CELL_RENDERERS = {
    text(product, column) {
        if (column.field === 'name') {
            return `<button class="product-name" data-action="product" data-id="${product.id}">${esc(product.name)}</button>`;
        }
        return esc(product[column.field]);
    },

    image(product) {
        return thumb(product);
    },

    product(product) {
        return `<button class="product-name" data-action="product" data-id="${product.id}">${esc(product.name)}</button>
            <span class="product-meta">${esc(product.manufacturerCode)}</span>`;
    },

    stock(product) {
        return stock(product);
    },

    money(product) {
        return `<span class="money">${money(product.price)}</span>`;
    },

    purchase(product) {
        return `<div class="purchase">
            ${quantityControl(product)}
            <button
                class="add-cart"
                data-action="add"
                data-id="${product.id}"
                ${product.stock === 0 ? 'disabled' : ''}
                aria-label="${esc(product.name)} sepete ekle">
                ${icon('cart')}
            </button>
        </div>`;
    },
};

function columnClass(column) {
    return [
        column.desktop ? '' : 'hide-desktop',
        column.tablet ? '' : 'hide-tablet',
        column.mobile ? '' : 'hide-mobile',
        column.field === 'name' ? 'product-cell' : '',
    ].filter(Boolean).join(' ');
}

function renderProductTable(data) {
    if (!data.items.length) {
        return empty(
            'Eşleşen ürün bulunamadı.',
            'Farklı bir kelime deneyin veya filtreleri temizleyin.',
            '<button class="button" data-action="clear-filters">Filtreleri temizle</button>',
        );
    }

    const columns = state.meta.columns;

    const headers = columns.map(column => `
        <th
            class="${columnClass(column)}"
            style="text-align:${column.align};width:${column.width}px">
            ${esc(column.label)}
        </th>
    `).join('');

    const rows = data.items.map(product => `
        <tr>
            ${columns.map(column => {
                const renderCell = CELL_RENDERERS[column.renderType] || CELL_RENDERERS.text;
                return `<td class="${columnClass(column)}" style="text-align:${column.align}">
                    ${renderCell(product, column)}
                </td>`;
            }).join('')}
        </tr>
    `).join('');

    return `<div class="table-scroll">
        <table class="data-table">
            <thead><tr>${headers}</tr></thead>
            <tbody>${rows}</tbody>
        </table>
    </div>
    ${pager(data.total, data.page)}`;
}

function renderSortOptions(filters) {
    return SORT_OPTIONS.map(([value, label]) => `
        <option value="${value}" ${filters.sort === value ? 'selected' : ''}>${label}</option>
    `).join('');
}

function renderActiveFilters(filters) {
    const hasFilters = filters.q || filters.brand || filters.stock || filters.category;

    return `<div class="active-filters">
        ${filters.q ? `<span class="filter-chip">Arama: ${esc(filters.q)}</span>` : ''}
        ${filters.brand ? `<span class="filter-chip">${esc(filters.brand)}</span>` : ''}
        ${hasFilters ? '<button class="text-button" data-action="clear-filters">Temizle ×</button>' : ''}
    </div>`;
}

export async function renderCatalogPage() {
    const isNewProducts = state.route === 'new-products';
    const filters = isNewProducts
        ? { ...state.filters, sort: 'newest' }
        : state.filters;

    const query = new URLSearchParams(
        Object.entries(filters).filter(([, value]) => value !== ''),
    );

    const data = await api('/products?' + query);
    state.products = data.items;

    const pageHeading = state.route === 'home'
        ? heading(
            `Merhaba, ${esc(state.user.firstName)}.`,
            'Bugün servisiniz için neye ihtiyacınız var?',
            `<a class="button" href="#orders">${icon('box')} Siparişlerim</a>`,
        ) + `<div id="banner">${renderBanner()}</div>`
        : heading(
            isNewProducts ? 'Yeni ürünler' : 'Ürün arama',
            isNewProducts
                ? 'Kataloğa son eklenen ürünler, en yeniden eskiye.'
                : 'Ürünleri bulun, adet girin ve doğrudan sepetinize ekleyin.',
        );

    return `${pageHeading}
        <div class="catalog-heading">
            <h2>Ürün kataloğu <span>${data.total} ürün</span></h2>
            <div class="muted"><small>Fiyatlar TL cinsindedir.</small></div>
        </div>

        <div class="catalog-layout">
            ${renderFilters()}
            <div>
                <section class="panel">
                    <div class="toolbar">
                        <label class="search">
                            ${icon('search')}
                            <input
                                id="catalog-search"
                                aria-label="Ürün ara"
                                placeholder="Ürün adı, kodu veya marka ile ara…"
                                value="${esc(filters.q)}"
                                maxlength="200">
                            <kbd>/</kbd>
                        </label>
                        <select
                            class="select"
                            data-filter="sort"
                            aria-label="Ürün sıralaması"
                            ${isNewProducts ? 'disabled' : ''}>
                            ${renderSortOptions(filters)}
                        </select>
                    </div>

                    ${renderActiveFilters(filters)}
                    ${renderProductTable(data)}
                </section>

                <div class="catalog-footnote">
                    <span>Stok bilgileri sipariş anında tekrar kontrol edilir.</span>
                    <span>Güvenli sipariş altyapısı</span>
                </div>
            </div>
        </div>`;
}

export async function showProduct(id) {
    const product = await api('/products/' + id);

    const specifications = [
        ['Ürün kodu', product.code],
        ['Üretici kodu', product.manufacturerCode],
        ['Özel kod 1', product.specialCode1 || '—'],
        ['Özel kod 2', product.specialCode2 || '—'],
        ['Marka', product.brand],
        ['Mevcut stok', product.stock + ' adet'],
    ].map(([label, value]) => `
        <div>
            <small>${label}</small>
            ${esc(value)}
        </div>
    `).join('');

    const photoCredit = product.imageUrl?.startsWith('/images/products/')
        ? '<p class="photo-caption">Temsili ürün fotoğrafı · <a href="/image-credits.html" target="_blank" rel="noopener">Kaynak ve lisans</a></p>'
        : '';

    modal('Ürün detayları', `
        <div class="detail-grid">
            <img class="detail-img" src="${esc(product.imageUrl)}" alt="${esc(product.name)}">
            <div class="detail-copy">
                <div class="eyebrow">${esc(product.category)} / ${esc(product.brand)}</div>
                <h2>${esc(product.name)}</h2>
                <p>${esc(product.description)}</p>
                ${photoCredit}
                <div class="specs">${specifications}</div>
                ${stock(product)}
            </div>
        </div>

        <div class="detail-buy">
            <span class="money">${money(product.price)}</span>
            <div class="purchase">
                ${quantityControl(product, 'detail')}
                <button class="button primary" data-action="add" data-id="${product.id}" ${!product.stock ? 'disabled' : ''}>
                    ${icon('cart')} Sepete ekle
                </button>
            </div>
        </div>
    `);
}
