import {
    api,
    closeModal,
    empty,
    esc,
    go,
    heading,
    icon,
    money,
    modal,
    refreshCart,
    state,
    thumb,
    toast,
} from './app-core.js?v=20260928a';
import { quantityControl } from './catalog.js?v=20260928a';
import {
    createDraft,
    noteKeyFor,
    parsePending,
    pendingKeyFor,
} from './checkout-state.js?v=20260923f';

let checkoutApproval = null;

function pendingKey() {
    return pendingKeyFor(state.user.id);
}

function noteKey() {
    return noteKeyFor(state.user.id);
}

export function draftNote() {
    if (!state.user) {
        return '';
    }

    return sessionStorage.getItem(noteKey()) ?? '';
}

export function saveDraftNote(value) {
    if (state.user) {
        sessionStorage.setItem(noteKey(), value);
    }
}

function storedApproval() {
    if (!state.user) {
        return null;
    }

    return parsePending(sessionStorage.getItem(pendingKey()));
}

function saveApproval(value) {
    checkoutApproval = value;

    if (value) {
        sessionStorage.setItem(pendingKey(), JSON.stringify(value));
        return;
    }

    if (state.user) {
        sessionStorage.removeItem(pendingKey());
    }
}

export function resetCheckoutApproval() {
    checkoutApproval = null;
}

export function clearCheckoutSession() {
    if (!state.user) {
        return;
    }

    sessionStorage.removeItem(pendingKey());
    sessionStorage.removeItem(noteKey());
    checkoutApproval = null;
}

export function onCheckoutDialogClosed() {
    if (checkoutApproval?.status === 'draft') {
        checkoutApproval = null;
    }
}

function approvalTotal(approval) {
    return approval.lines.reduce(
        (sum, line) => sum + Math.round(line.unitPrice * 100) * line.quantity,
        0,
    ) / 100;
}

function changedLines(approval, previousApproval) {
    if (!previousApproval) {
        return [];
    }

    const changed = approval.lines
        .filter(line => {
            const previous = previousApproval.lines.find(item => item.productId === line.productId);
            return !previous ||
                previous.quantity !== line.quantity ||
                previous.unitPrice !== line.unitPrice;
        })
        .map(line => `${line.name}: ${line.quantity} adet · ${money(line.unitPrice)}`);

    const removed = previousApproval.lines
        .filter(previous => !approval.lines.some(line => line.productId === previous.productId))
        .map(line => `${line.name}: sepetten çıkarıldı`);

    return [...changed, ...removed];
}

function renderCheckoutDialog(approval, previousApproval = null) {
    const isPending = approval.status !== 'draft';
    const currentTotal = approvalTotal(approval);
    const previousTotal = previousApproval ? approvalTotal(previousApproval) : 0;
    const changes = changedLines(approval, previousApproval);

    let notice;
    if (isPending) {
        notice = 'Önceki sipariş isteğinin sonucu bilinmiyor. Aynı istek yeniden sorgulanacak; yeni sipariş başlatılmayacak.';
    } else if (previousApproval) {
        const changeList = changes.length
            ? `<ul>${changes.map(change => `<li>${esc(change)}</li>`).join('')}</ul>`
            : '';

        notice = `Önceki onay geçersiz. Önceki toplam ${money(previousTotal)}; güncel sepeti inceleyin.${changeList}`;
    } else {
        notice = 'Aşağıdaki ürün, adet ve fiyatları onaylayın.';
    }

    const title = isPending
        ? 'Önceki siparişi sorgula'
        : previousApproval
            ? 'Sepet değişti, yeniden onaylayın'
            : 'Siparişinizi onaylayın';

    const rows = approval.lines.map(line => `
        <tr>
            <td>${esc(line.name)}</td>
            <td>${line.quantity}</td>
            <td>${money(line.unitPrice)}</td>
        </tr>
    `).join('');

    modal(title, `
        <div class="notice" role="status">${notice}</div>

        <div class="table-scroll">
            <table class="data-table">
                <thead>
                    <tr>
                        <th>Ürün</th>
                        <th>Adet</th>
                        <th>Birim fiyat</th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>
        </div>

        <div class="summary-row summary-total">
            <span>${previousApproval ? 'Yeni toplam' : 'Toplam'}</span>
            <span>${money(currentTotal)}</span>
        </div>

        <form data-form="checkout" data-request-id="${approval.requestId}">
            <div class="form-actions">
                <button type="button" class="button" data-action="close">Geri dön</button>
                <button class="button primary" type="submit">
                    ${icon('check')} ${isPending ? 'Önceki siparişi sorgula' : 'Siparişi oluştur'}
                </button>
            </div>
        </form>
    `);
}

export async function openCheckout(
    note,
    { changed = false, previousApproval = null, renderPage } = {},
) {
    await refreshCart();

    const pending = changed ? null : storedApproval();
    const unavailable = state.cart.items.filter(product => product.isArchived);

    if (unavailable.length && !pending) {
        closeModal();
        if (renderPage) {
            await renderPage();
        }
        toast('Sepetinizde artık satışta olmayan ürün var. Bu ürünü sepetten çıkarın.', true);
        return;
    }

    if (!state.cart.items.length && !pending) {
        closeModal();
        if (renderPage) {
            await renderPage();
        }
        toast('Sepetiniz boş.', true);
        return;
    }

    const approval =
        pending ||
        createDraft(state.cart, note, crypto.randomUUID());

    checkoutApproval = approval;
    renderCheckoutDialog(approval, previousApproval);
}

export async function renderCartPage() {
    await refreshCart();

    const cart = state.cart;
    const unavailable = cart.items.filter(product => product.isArchived);

    if (!cart.items.length) {
        const action = storedApproval()
            ? '<button class="button primary" data-action="checkout">Bekleyen siparişi sorgula</button>'
            : '<a class="button primary" href="#catalog">Ürünleri keşfet</a>';

        return `<section class="panel">
            ${empty(
                'Sepetiniz henüz boş.',
                'Ürün kataloğundan ihtiyaç duyduğunuz parçaları ekleyin.',
                action,
            )}
        </section>`;
    }

    const rows = cart.items.map(product => `
        <tr>
            <td>
                <div class="cart-product">
                    ${thumb(product)}
                    <div>
                        ${product.isArchived
                            ? `<span class="product-name">${esc(product.name)}</span>`
                            : `<button class="product-name" data-action="product" data-id="${product.id}">
                                ${esc(product.name)}
                              </button>`}
                        <span class="product-meta">${esc(product.code)}</span>
                        ${product.isArchived
                            ? `<span class="stock empty">Artık satışta değil · Sepetten çıkarın</span>`
                            : product.quantity > product.stock
                                ? `<span class="stock empty">Stok yetersiz: ${product.stock}</span>`
                                : ''}
                    </div>
                </div>
            </td>
            <td class="money" data-label="Birim fiyat">${money(product.price)}</td>
            <td data-label="Adet">${product.isArchived ? `${product.quantity} adet` : quantityControl(product, 'cart')}</td>
            <td class="money" data-label="Toplam">${money(product.total)}</td>
            <td data-label="Sepetten çıkar">
                <button
                    class="icon-button"
                    data-action="remove"
                    data-id="${product.id}"
                    aria-label="${esc(product.name)} sepetten çıkar">
                    ${icon('trash')}
                </button>
            </td>
        </tr>
    `).join('');

    return heading(
        'Sepetim',
        `${cart.items.length} farklı ürün · ${cart.count} adet`,
        `<a class="button" href="#catalog">Alışverişe devam et ${icon('arrow')}</a>`,
    ) + `<div class="cart-layout">
        <section class="panel">
            ${unavailable.length
                ? `<div class="notice error" role="alert">Sepetinizde artık satışta olmayan ürün var. Sipariş vermeden önce kırmızı uyarılı ürünü sepetten çıkarın.</div>`
                : ''}
            <div class="table-scroll">
                <table class="data-table cart-table">
                    <thead>
                        <tr>
                            <th>Ürün</th>
                            <th>Birim fiyat</th>
                            <th>Adet</th>
                            <th>Toplam</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>${rows}</tbody>
                </table>
            </div>

            <div class="note-input">
                <label for="order-note">
                    Sipariş notu <small class="muted">(isteğe bağlı)</small>
                </label>
                <textarea
                    id="order-note"
                    class="input"
                    rows="2"
                    maxlength="1000"
                    placeholder="Siparişinizle ilgili belirtmek istedikleriniz…">${esc(draftNote())}</textarea>
            </div>
        </section>

        <aside class="panel summary">
            <h2>Sipariş özeti</h2>
            <div class="summary-row"><span>Ürün adedi</span><b>${cart.count}</b></div>
            <div class="summary-row"><span>Ara toplam</span><b>${money(cart.total)}</b></div>
            <div class="summary-row summary-total"><span>Toplam</span><span>${money(cart.total)}</span></div>
            <button class="button primary" data-action="checkout" ${unavailable.length ? 'disabled' : ''}>
                ${unavailable.length ? 'Satıştan kaldırılan ürünü çıkarın' : `Siparişi gözden geçir ${icon('arrow')}`}
            </button>
            <p class="summary-note">
                Siparişiniz bayi yöneticisinin onayına gönderilir. Bu aşamada online ödeme alınmaz.
            </p>
        </aside>
    </div>`;
}

export async function submitCheckout({ renderPage } = {}) {
    const approval = storedApproval() || checkoutApproval;

    if (!approval) {
        throw new Error('Sipariş onayı bulunamadı. Sepeti yeniden gözden geçirin.');
    }

    if (approval.status === 'draft') {
        saveApproval({ ...approval, status: 'sending' });
    }

    try {
        const order = await api('/orders', {
            method: 'POST',
            body: {
                requestId: approval.requestId,
                note: approval.note,
                lines: approval.lines.map(({ productId, quantity, unitPrice }) => ({
                    productId,
                    quantity,
                    unitPrice,
                })),
            },
        });

        saveApproval(null);
        saveDraftNote('');
        await refreshCart();
        closeModal();
        go('orders');
        toast(`Sipariş oluşturuldu: ${order.number}`);
    } catch (error) {
        if (error.code === 'CART_CHANGED') {
            const note = approval.note;
            saveApproval(null);
            await openCheckout(note, {
                changed: true,
                previousApproval: approval,
                renderPage,
            });
            return;
        }

        if (error.code === 'PRODUCT_ARCHIVED') {
            saveApproval(null);
            await refreshCart();
            closeModal();
            if (renderPage) {
                await renderPage();
            }
            throw error;
        }

        if (error.status && error.status < 500 && error.code !== 'REQUEST_BUSY') {
            saveApproval(null);
        } else {
            const unknown = { ...approval, status: 'unknown' };
            saveApproval(unknown);
            renderCheckoutDialog(unknown);
        }

        throw error;
    }
}
