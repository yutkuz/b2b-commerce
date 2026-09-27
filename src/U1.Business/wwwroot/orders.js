import {
    api,
    date,
    empty,
    esc,
    heading,
    icon,
    modal,
    money,
    pager,
    state,
    status,
} from './app-core.js?v=20260928a';

export function renderOrdersTable(items, admin = false) {
    if (!items.length) {
        return empty(
            'Henüz sipariş yok.',
            'Oluşturulan siparişler burada listelenecek.',
        );
    }

    const rows = items.map(order => `
        <tr>
            <td>
                <button class="row-link mono" data-action="order" data-id="${order.id}">
                    ${esc(order.number)}
                </button>
            </td>
            ${admin ? `
                <td class="cell-stack">
                    <b>${esc(order.company)}</b>
                    <small>${esc(order.firstName)} ${esc(order.lastName)}</small>
                </td>
            ` : ''}
            <td>${date(order.createdAt)}</td>
            <td class="money">${money(order.total)}</td>
            <td>${status(order.status)}</td>
            <td>
                <button class="icon-button" data-action="order" data-id="${order.id}" aria-label="Sipariş detayını aç">
                    ${icon('chevron')}
                </button>
            </td>
        </tr>
    `).join('');

    return `<div class="table-scroll">
        <table class="data-table">
            <thead>
                <tr>
                    <th>Sipariş numarası</th>
                    ${admin ? '<th>Bayi</th>' : ''}
                    <th>Tarih</th>
                    <th>Toplam tutar</th>
                    <th>Durum</th>
                    <th></th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
    </div>`;
}

export async function renderOrdersPage() {
    const page = +(state.params.get('page') || 1);
    const data = await api('/orders?page=' + page);

    return heading(
        'Siparişlerim',
        'Tüm siparişleriniz ve güncel durumları.',
        `<a class="button primary" href="#catalog">${icon('plus')} Yeni sipariş</a>`,
    ) + `<section class="panel">
        ${renderOrdersTable(data.items)}
        ${pager(data.total, page)}
    </section>`;
}

export async function showOrder(id) {
    const data = await api('/orders/' + id);
    const order = data.order;

    const rows = data.items.map(item => `
        <tr>
            <td class="cell-stack">
                <b>${esc(item.productName)}</b>
                <small>${esc(item.productCode)}</small>
            </td>
            <td>${item.quantity}</td>
            <td class="money">${money(item.unitPrice)}</td>
            <td class="money">${money(item.total)}</td>
        </tr>
    `).join('');

    const statusForm =
        state.user.role === 'Admin' &&
        state.route.startsWith('admin') &&
        order.status !== 'Reddedildi'
            ? `<form data-form="order-status" data-id="${order.id}" class="form-actions">
                <select name="status" class="select" aria-label="Sipariş durumu">
                    <option value="Onaylandı">Onaylandı</option>
                    <option value="Reddedildi">Reddedildi · stok iade edilir</option>
                </select>
                <button class="button primary" type="submit">Durumu güncelle</button>
            </form>`
            : '';

    modal('Sipariş detayları', `
        <div class="eyebrow">${esc(order.number)}</div>

        <div class="meta-line">
            <div>
                <small>Bayi</small>
                <b>${esc(order.company || order.firstName + ' ' + order.lastName)}</b>
            </div>
            <div>
                <small>Sipariş tarihi</small>
                <b>${date(order.createdAt)}</b>
            </div>
            <div>
                <small>Durum</small>
                ${status(order.status)}
            </div>
        </div>

        <div class="table-scroll">
            <table class="data-table">
                <thead>
                    <tr>
                        <th>Ürün / Kod</th>
                        <th>Adet</th>
                        <th>Birim fiyat</th>
                        <th>Toplam</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>

        <div class="detail-total">${money(order.total)}</div>
        ${order.note ? `<div class="order-note">${esc(order.note)}</div>` : ''}
        <p class="muted"><small>Ürün ve fiyat bilgileri sipariş oluşturulduğu andaki kayıtlardır.</small></p>
        ${statusForm}
    `);
}
