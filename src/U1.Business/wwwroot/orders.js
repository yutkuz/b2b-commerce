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
    const isAdmin = state.user.role === 'Admin' && state.route.startsWith('admin');
    const internal = isAdmin ? await api('/admin/orders/' + id + '/internal') : null;

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

    const nextStatuses = {
        'Bekliyor': ['Onaylandı', 'Reddedildi', 'İptal edildi'],
        'Onaylandı': ['Hazırlanıyor', 'Reddedildi', 'İptal edildi'],
        'Hazırlanıyor': ['Sevk edildi', 'Reddedildi', 'İptal edildi'],
        'Sevk edildi': ['Teslim edildi'],
    }[order.status] || [];
    const statusForm = isAdmin && nextStatuses.length
            ? `<form data-form="order-status" data-id="${order.id}" class="form-actions order-controls">
                <input type="hidden" name="rowVersion" value="${esc(order.rowVersion)}" />
                <select name="status" class="select" aria-label="Sipariş durumu">
                    ${nextStatuses.map(value => `<option value="${esc(value)}">${esc(value)}</option>`).join('')}
                </select>
                <input class="input" name="reason" aria-label="Ret veya iptal nedeni" maxlength="300"
                    placeholder="Ret veya iptal için neden" />
                <button class="button primary" type="submit">Durumu güncelle</button>
            </form>`
            : '';
    const history = data.history.map(item => `
        <li>${esc(item.fromStatus || 'Başlangıç')} → ${esc(item.toStatus)} · ${date(item.changedAt)}
            ${item.reason ? ` · ${esc(item.reason)}` : ''}</li>`).join('');
    const noteForm = isAdmin ? `<form data-form="order-admin-note" data-id="${order.id}"
        class="order-controls">
        <input type="hidden" name="rowVersion" value="${esc(order.rowVersion)}" />
        <label for="admin-order-note">Yönetici notu (bayiye gösterilmez)</label>
        <textarea class="input" id="admin-order-note" name="note" maxlength="1000">${esc(internal.adminNote)}</textarea>
        <button class="button" type="submit">Notu kaydet</button>
    </form>` : '';

    modal('Sipariş detayları', `
        <div class="order-printable">
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
        ${order.rejectionReason ? `<div class="notice error">Ret/iptal nedeni: ${esc(order.rejectionReason)}</div>` : ''}
        <h3>Durum geçmişi</h3>
        <ol class="order-history">${history}</ol>
        <p class="muted"><small>Ürün ve fiyat bilgileri sipariş oluşturulduğu andaki kayıtlardır.</small></p>
        </div>
        <div class="form-actions order-controls">
            <button class="button" type="button" data-action="print-order">Yazdır / PDF</button>
            ${!isAdmin ? `<button class="button" type="button" data-action="readd-preview" data-id="${order.id}">Yeniden sepete ekle</button>` : ''}
        </div>
        ${statusForm}
        ${noteForm}
    `);
}

export async function showReaddPreview(id) {
    const data = await api('/orders/' + id + '/readd-preview');
    const rows = data.items.map(item => `
        <tr>
            <td><label><input type="checkbox" data-readd-line data-product-id="${item.productId}"
                data-quantity="${item.quantity}" data-price="${item.currentUnitPrice}"
                ${item.canAdd ? 'checked' : 'disabled'} /> ${esc(item.productName)}</label></td>
            <td>${item.quantity}</td>
            <td>${money(item.previousUnitPrice)}</td>
            <td>${money(item.currentUnitPrice)}</td>
            <td>${item.isArchived ? 'Satışta değil' : !item.canAdd ? 'Stok/adet sınırı yetersiz' : 'Uygun'}</td>
        </tr>`).join('');
    modal('Eski siparişten sepete ekle', `
        <p>Güncel fiyat ve stokları kontrol edip istediğiniz uygun kalemleri seçin. Yeni sipariş oluşturulmaz.</p>
        <div class="table-scroll"><table class="data-table">
            <thead><tr><th>Ürün</th><th>Adet</th><th>Eski fiyat</th><th>Güncel fiyat</th><th>Durum</th></tr></thead>
            <tbody>${rows}</tbody>
        </table></div>
        <div class="form-actions">
            <button class="button primary" type="button" data-action="confirm-readd" data-id="${id}">
                Seçilenleri sepete ekle
            </button>
        </div>`);
}
