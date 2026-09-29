import { adminAction, adminSubmit } from './admin.js?v=20260928b';
import {
    api,
    closeModal,
    formData,
    go,
    refreshCart,
    resetCsrf,
    restoreModalFocus,
    state,
    toast,
} from './app-core.js?v=20260928a';
import {
    renderBanner,
    selectedBanner,
    showProduct,
} from './catalog.js?v=20260928a';
import {
    clearCheckoutSession,
    draftNote,
    onCheckoutDialogClosed,
    openCheckout,
    resetCheckoutApproval,
    saveDraftNote,
    submitCheckout,
} from './checkout.js?v=20260928a';
import { showOrder } from './orders.js?v=20260928a';

let renderPage;
let resetFiltersHandler;
let catalogRoutes;
let searchTimer;

async function logout() {
    clearCheckoutSession();
    await api('/auth/logout', { method: 'POST' });

    state.user = null;
    state.meta = null;
    resetFiltersHandler();
    resetCsrf();

    go('login');
}

async function updateCartQuantity(input, quantity) {
    await api('/cart', {
        method: 'PUT',
        body: {
            productId: +input.dataset.quantity,
            quantity,
        },
    });
}

function catalogPageAction(page) {
    if (catalogRoutes.has(state.route)) {
        state.filters.page = page;
        return renderPage();
    }

    state.params.set('page', page);
    go(state.route + '?' + state.params);
}

async function handleActionClick(event) {
    const element = event.target.closest('[data-action]');

    if (!element || element.disabled) {
        return;
    }

    const action = element.dataset.action;
    const id = +element.dataset.id;

    try {
        switch (action) {
            case 'skip':
                event.preventDefault();
                document.querySelector('#main')?.focus();
                return;

            case 'close':
                closeModal();
                return;

            case 'portal-menu': {
                const navigation = document.querySelector('#portal-nav');
                const isOpen = navigation.classList.toggle('open');
                element.setAttribute('aria-expanded', String(isOpen));
                return;
            }

            case 'logout':
                await logout();
                return;

            case 'retry':
                await renderPage();
                return;

            case 'product':
                await showProduct(id);
                return;

            case 'order':
                await showOrder(id);
                return;

            case 'category':
                state.filters.category = element.dataset.id;
                state.filters.page = 1;
                await renderPage();
                return;

            case 'clear-filters':
                resetFiltersHandler();
                await renderPage();
                return;

            case 'page':
                await catalogPageAction(+element.dataset.page);
                return;

            case 'banner-slide':
                state.bannerId = state.meta.banners[+element.dataset.index]?.id ?? null;
                document.querySelector('#banner').innerHTML = renderBanner();
                return;

            case 'banner-go': {
                const banner = selectedBanner().banner;
                if (!banner) {
                    return;
                }

                state.filters.q = banner.searchTerm;
                state.filters.page = 1;
                go('catalog');
                return;
            }

            case 'quantity': {
                const input = element.parentElement.querySelector('input');
                const nextValue = Math.max(
                    1,
                    Math.min(
                        +input.max,
                        Number(input.value) + Number(element.dataset.delta),
                    ),
                );

                input.value = nextValue;

                if (input.dataset.context === 'cart') {
                    await updateCartQuantity(input, nextValue);
                    await renderPage();
                }
                return;
            }

            case 'add': {
                const input = element.closest('.purchase').querySelector('input');
                const quantity = Number(input.value);

                if (!Number.isInteger(quantity) || quantity < 1) {
                    throw new Error('Geçerli bir adet girin.');
                }

                element.disabled = true;

                await api('/cart', {
                    method: 'POST',
                    body: {
                        productId: id,
                        quantity,
                    },
                });

                await refreshCart();
                toast(`${quantity} adet ürün sepetinize eklendi.`);
                return;
            }

            case 'remove':
                await api('/cart/' + id, { method: 'DELETE' });
                await renderPage();
                toast('Ürün sepetten çıkarıldı.');
                return;

            case 'checkout':
                await openCheckout(
                    document.querySelector('#order-note')?.value ?? draftNote(),
                    { renderPage },
                );
                return;

            default:
                await adminAction(action, element, { render: renderPage });
        }
    } catch (error) {
        toast(error.message, true);
    } finally {
        if (action === 'add') {
            element.disabled = false;
        }
    }
}

async function handleChange(event) {
    const input = event.target;

    try {
        if (input.dataset.filter) {
            const value =
                input.type === 'checkbox' && !input.checked
                    ? ''
                    : input.value;

            state.filters[input.dataset.filter] = value;
            state.filters.page = 1;

            await renderPage();
            return;
        }

        if (input.dataset.context === 'cart') {
            await updateCartQuantity(input, Number(input.value));
            await renderPage();
            return;
        }

        if (input.id === 'product-image-file' && input.files[0]) {
            const body = new FormData();
            body.append('file', input.files[0]);

            const result = await api('/admin/images', {
                method: 'POST',
                body,
            });

            document.querySelector('[name=imageUrl]').value = result.url;
            document.querySelector('.upload-preview').src = result.url;
            toast('Görsel yüklendi.');
        }
    } catch (error) {
        toast(error.message, true);

        if (input.dataset.context === 'cart') {
            await renderPage();
        }
    }
}

function handleInput(event) {
    const input = event.target;

    if (input.id === 'order-note') {
        saveDraftNote(input.value);
    }

    if (input.id !== 'catalog-search') {
        return;
    }

    clearTimeout(searchTimer);

    state.filters.q = input.value;
    state.filters.page = 1;

    searchTimer = setTimeout(renderPage, 300);
}

async function submitAuthForm(kind, data) {
    state.user = await api('/auth/' + kind, {
        method: 'POST',
        body: data,
    });

    resetCsrf();
    state.meta = null;
    resetCheckoutApproval();

    await refreshCart();
    go('home');
}

async function submitOrderStatus(form, data) {
    await api('/admin/orders/' + form.dataset.id + '/status', {
        method: 'PUT',
        body: data,
    });

    closeModal();
    await renderPage();
    toast('Sipariş durumu güncellendi.');
}

async function handleSubmit(event) {
    const form = event.target;

    if (!form.dataset.form) {
        return;
    }

    event.preventDefault();

    const submitButton = form.querySelector('[type=submit]');
    if (submitButton?.disabled) {
        return;
    }

    if (submitButton) {
        submitButton.disabled = true;
    }

    try {
        const data = formData(form);
        const kind = form.dataset.form;

        switch (kind) {
            case 'header-search':
                state.filters = {
                    q: data.q.trim(),
                    category: '',
                    brand: '',
                    stock: '',
                    sort: '',
                    page: 1,
                };
                go('catalog');
                return;

            case 'login':
            case 'register':
                await submitAuthForm(kind, data);
                return;

            case 'checkout':
                await submitCheckout({ renderPage });
                return;

            case 'order-status':
                await submitOrderStatus(form, data);
                return;

            default:
                await adminSubmit(kind, form, data, { render: renderPage });
        }
    } catch (error) {
        const authError = form.querySelector('#auth-error');

        if (authError) {
            authError.textContent = error.message;
        } else {
            toast(error.message, true);
        }
    } finally {
        if (submitButton) {
            submitButton.disabled = false;
        }
    }
}

function handleSearchShortcut(event) {
    if (
        event.key !== '/' ||
        ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement.tagName)
    ) {
        return;
    }

    const search =
        document.querySelector('#catalog-search') ||
        document.querySelector('#global-search');

    if (!search) {
        return;
    }

    event.preventDefault();
    search.focus();
}

function handleImageError(event) {
    if (
        event.target.tagName === 'IMG' &&
        !event.target.src.endsWith('/images/product.svg')
    ) {
        event.target.src = '/images/product.svg';
    }
}

function closeNativeMenus(event) {
    document.querySelectorAll('.native-menu[open]').forEach(menu => {
        if (!menu.contains(event.target) || event.target.closest('a,button')) {
            menu.open = false;
        }
    });
}

function handleNativeMenuEscape(event) {
    if (event.key !== 'Escape') {
        return;
    }

    document.querySelectorAll('.native-menu[open]').forEach(menu => {
        menu.open = false;
        menu.querySelector('summary').focus();
    });
}

function handleDialogCancel(event) {
    event.preventDefault();
    closeModal();
}

function handleDialogBackdropClick(event) {
    if (event.target !== event.currentTarget) {
        return;
    }

    const bounds = event.currentTarget.getBoundingClientRect();
    const outsideDialog =
        event.clientX < bounds.left ||
        event.clientX > bounds.right ||
        event.clientY < bounds.top ||
        event.clientY > bounds.bottom;

    if (outsideDialog) {
        closeModal();
    }
}

export function bindAppEvents({ render, resetFilters, catalogRouteNames }) {
    renderPage = render;
    resetFiltersHandler = resetFilters;
    catalogRoutes = catalogRouteNames;

    document.addEventListener('click', handleActionClick);
    document.addEventListener('change', handleChange);
    document.addEventListener('input', handleInput);
    document.addEventListener('submit', handleSubmit);
    document.addEventListener('keydown', handleSearchShortcut);
    document.addEventListener('keydown', handleNativeMenuEscape);
    document.addEventListener('error', handleImageError, true);
    document.addEventListener('click', closeNativeMenus);

    const dialog = document.querySelector('#dialog');
    dialog.addEventListener('cancel', handleDialogCancel);
    dialog.addEventListener('click', handleDialogBackdropClick);
    dialog.addEventListener('close', () => {
        onCheckoutDialogClosed();
        restoreModalFocus();
    });
}
