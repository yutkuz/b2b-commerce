import { company, documents } from './content.js?v=20260923f';
import { state, esc, icon, money, heading, field } from './app-core.js?v=20260928a';

export const portalRoutes = {
    'new-products': ['Yeni ürünler', 'grid'],
    account: ['Hesabım', 'users'],
    payment: ['Online ödeme', 'card'],
    banks: ['Banka bilgileri', 'bank'],
    support: ['Destek', 'help'],
    about: ['Hakkımızda', 'box'],
    returns: ['İade şartları', 'box'],
    privacy: ['Gizlilik politikası', 'shield'],
    terms: ['Satış sözleşmesi', 'shield'],
};

export const publicRoutes = ['about', 'returns', 'privacy', 'terms', 'support'];

export function portalFooter() {
    return `<footer class="portal-footer">
        <div class="footer-links"><a class="footer-wordmark" href="#home">u1<span>business</span></a>
            <nav aria-label="Kurumsal bağlantılar">
                <a href="#about">Hakkımızda</a><a href="#returns">İade şartları</a>
                <a href="#privacy">Gizlilik politikası</a><a href="#terms">Satış sözleşmesi</a><a href="#support">Firma desteği</a>
            </nav>
        </div>
        <div class="footer-bottom"><span>© ${new Date().getFullYear()} U1 Business. Tüm hakları saklıdır.</span><a href="/image-credits.html" target="_blank" rel="noopener">Görsel kaynakları</a></div>
    </footer>`;
}

export function portalShell(paths, brand) {
    const u = state.user;
    const admin = state.route.startsWith('admin');
    const nav = (route, label) => `<a href="#${route}" class="${state.route === route ? 'active' : ''}" ${state.route === route ? 'aria-current="page"' : ''}>${label}</a>`;
    return `<div class="portal-layout" data-shell-route="${state.route}">
        <div class="portal-utility"><div><span>Bayiye özel ürün ve sipariş platformu</span><span>${esc(u.company || u.firstName + ' ' + u.lastName)}<span class="utility-divider">/</span>${u.role === 'Admin' ? 'Yönetici' : 'Bayi hesabı'}</span></div></div>
        <header class="portal-header">
            <div class="portal-masthead">${brand()}
                <form class="header-search" data-form="header-search" role="search">
                    <label for="global-search" class="sr-only">Tüm ürünlerde ara</label>
                    <input id="global-search" name="q" type="search" placeholder="Ürün adı, stok kodu veya marka…" maxlength="200" autocomplete="off">
                    <button type="submit" aria-label="Ürünleri ara">${icon('search')}</button>
                </form>
                <div class="account-tools">
                    <a class="balance-tool" href="#account" aria-label="Toplam bakiye, cari hesap bilgileri"><span>Toplam bakiye ${icon('chevron')}</span><strong>— <small>Cari hesap bağlı değil</small></strong></a>
                    <a class="cart-tool" href="#cart" aria-label="Sepetim"><span class="cart-symbol">${icon('cart')}<b data-cart-count>${state.cart.count}</b></span><span><small>Sepetim</small><strong data-cart-total>${money(state.cart.total)}</strong></span></a>
                    <a class="support-tool" href="#support">${icon('help')}<span>Destek</span></a>
                    <details class="native-menu account-menu"><summary aria-label="Hesap menüsü">${icon('more')}</summary><div class="dropdown-panel"><div class="dropdown-user"><b>${esc(u.firstName)} ${esc(u.lastName)}</b><small>${esc(u.email)}</small></div><a href="#account">${icon('users')} Hesabım</a>${u.role === 'Admin' ? '<a href="#admin">'+icon('sliders')+' Yönetim paneli</a>' : ''}<a href="#support">${icon('help')} Kullanım yardımı</a><button data-action="logout">${icon('logout')} Çıkış yap</button></div></details>
                </div>
            </div>
            <div class="mobile-account-bar"><a href="#account"><span>Toplam bakiye</span><b>—</b></a><a href="#cart">${icon('cart')}<span>Sepet (<span data-cart-count>${state.cart.count}</span>)</span><b data-cart-total>${money(state.cart.total)}</b></a></div>
            <div class="portal-nav-bar"><div class="portal-nav-inner"><button class="portal-menu-toggle" data-action="portal-menu" aria-controls="portal-nav" aria-expanded="false">${icon('menu')} Menü <span>${esc(paths[state.route]?.[0] || '')}</span></button>
                <nav class="portal-nav" id="portal-nav" aria-label="Ana menü">
                    ${nav('home','Ana sayfa')}${nav('catalog','Arama')}${nav('orders','Siparişler')}${nav('new-products','Yeni ürünler')}${nav('payment','Online ödeme')}${nav('banks','Banka bilgileri')}
                    <details class="native-menu other-menu"><summary>Diğer ${icon('down')}</summary><div class="dropdown-panel"><a href="#account">Hesabım</a><a href="#support">Kullanım yardımı</a><a href="#about">Hakkımızda</a><a href="#returns">İade şartları</a>${u.role === 'Admin' ? '<a href="#admin">Yönetim paneli</a>' : ''}</div></details>
                </nav><span class="nav-date">${new Date().toLocaleDateString('tr-TR',{day:'numeric',month:'long',year:'numeric'})}</span>
            </div></div>
        </header>
        ${admin ? `<div class="admin-navigation"><nav aria-label="Yönetim menüsü"><span>YÖNETİM</span>${['admin','admin-products','admin-categories','admin-orders','admin-users','admin-grid','admin-banners','admin-history'].map(r => nav(r,paths[r][0])).join('')}</nav></div>` : ''}
        <main class="content portal-content" id="main" tabindex="-1"><div class="portal-breadcrumb"><a href="#home">Ana sayfa</a>${state.route === 'home' ? '<span>/ Bayi portalı</span>' : '<span>/</span><span>'+esc(paths[state.route][0])+'</span>'}</div><div id="page"><div class="loading">Yükleniyor…</div></div></main>
        ${portalFooter()}
    </div>`;
}

export function publicShell(brand) {
    return `<div class="portal-layout"><header class="public-header">${brand()}<a class="button" href="#login">Bayi girişi ${icon('arrow')}</a></header><main class="content portal-content" id="main" tabindex="-1"><div id="page">${renderPortalPage()}</div></main>${portalFooter()}</div>`;
}

function informationPanel(iconName, title, description, action = '') {
    return `<section class="information-panel"><div class="information-icon">${icon(iconName)}</div><h2>${title}</h2><p>${description}</p>${action}</section>`;
}

export function renderPortalPage() {
    const route = state.route;
    if (route === 'account') {
        const u = state.user;
        return heading('Hesabım', 'Firma ve iletişim bilgilerinizi güncelleyin.') +
            `<div class="account-layout">
                <section class="panel account-card">
                    <div class="section-title"><h2>Profil bilgileri</h2><span class="status-badge approved">Aktif hesap</span></div>
                    <form data-form="profile" data-version="${esc(u.rowVersion || '')}">
                        <div class="form-grid">
                            ${field('Ad', 'firstName', u.firstName, 'text', 'required maxlength="80" autocomplete="given-name"')}
                            ${field('Soyad', 'lastName', u.lastName, 'text', 'required maxlength="80" autocomplete="family-name"')}
                            <div class="full">${field('Firma adı', 'company', u.company || '', 'text', 'maxlength="180" autocomplete="organization"')}</div>
                            <div class="full">${field('Telefon', 'phone', u.phone, 'tel', 'required maxlength="25" autocomplete="tel"')}</div>
                        </div>
                        <dl class="account-fields">
                            <div><dt>E-posta</dt><dd>${esc(u.email)}</dd></div>
                            <div><dt>Hesap türü</dt><dd>${u.role === 'Admin' ? 'Yönetici' : 'Bayi'}</dd></div>
                        </dl>
                        <p class="account-hint">E-posta, hesap türü ve aktiflik bu ekrandan değiştirilemez.</p>
                        <div class="form-actions"><button class="button primary" type="submit">${icon('check')} Bilgileri kaydet</button></div>
                    </form>
                </section>
                <section class="panel account-card">
                    <div class="section-title"><h2>Cari hesap</h2></div>
                    <div class="balance-state"><span>Toplam bakiye</span><strong>—</strong><p>Cari hesap bilgisi henüz paylaşılmadı. Borç ve alacak tutarlarını firma yetkilinizden öğrenebilirsiniz.</p><a class="button" href="#orders">Siparişlerimi görüntüle ${icon('arrow')}</a></div>
                </section>
            </div>`;
    }
    if (route === 'payment') {
        return heading('Online ödeme', 'Bayi hesabınıza ait ödeme işlemleri.') + informationPanel('card','Online ödeme henüz kullanıma açılmadı.','Bu portal üzerinden şu anda kartla ödeme alınmıyor. Siparişlerinizi oluşturmaya devam edebilirsiniz. Ödeme yöntemi için firma yetkilinizle iletişime geçin.',`<div class="information-actions"><a class="button primary" href="#cart">Sepetime git ${icon('arrow')}</a><a class="button" href="#banks">Banka bilgileri</a></div>`);
    }
    if (route === 'banks') {
        return heading('Banka bilgileri', 'Havale ve EFT hesap bilgileri.') + `<section class="panel institutional-page"><div class="document-status">Örnek hesap kartı · Ödemeye kapalı</div><h2>Türk lirası hesabı</h2><dl class="contact-details"><dt>Banka / Şube</dt><dd>Örnek Banka · Merkez Şube</dd><dt>Hesap sahibi</dt><dd>${company.name}</dd><dt>IBAN</dt><dd>TR•• •••• •••• •••• •••• •••• ••</dd><dt>Açıklama</dt><dd>Firma adı ve sipariş numarası</dd></dl><div class="institutional-note">Bu bilgiler yalnızca örnek gösterimdir. Gerçek banka hesabı bağlı değildir; ödeme için firma yetkilinizden teyit alın.</div></section>`;
    }
    if (route === 'support') {
        return heading('Destek', 'Sipariş ve hesap işlemleriniz için kısa kullanım rehberi.') + `<div class="help-layout"><section class="panel help-list"><h2>Sık sorulan sorular</h2>${[
            ['Ürünleri nasıl bulabilirim?','Üstteki arama alanına ürün adı, ürün kodu, üretici kodu veya marka yazın. Arama ekranında kategori ve stok filtrelerini kullanabilirsiniz.'],
            ['Nasıl sipariş oluşturabilirim?','Ürün satırında adet seçip sepet simgesine basın. Sepetinizde ürünleri kontrol edin, “Siparişi gözden geçir” ile özeti açın ve siparişi onaylayın.'],
            ['Stok göstergeleri ne anlama geliyor?','Var: stok yeterli. Kritik: stok ürünün belirlenen kritik seviyesinde veya altında. Yok: ürün şu anda siparişe kapalı. Son stok kontrolü sipariş oluşturulurken yapılır.'],
            ['Siparişimin durumunu nereden takip ederim?','Siparişler ekranında Bekliyor, Onaylandı veya Reddedildi durumunu görebilirsiniz. Sipariş numarasına tıklayarak kalemleri ve fiyatları inceleyebilirsiniz.'],
            ['Hesap bilgilerimi nasıl değiştirebilirim?','Ad, firma, iletişim bilgileri veya şifre değişikliği için firma yöneticinizle görüşün. Yönetici ilgili bilgileri bayi yönetiminden güncelleyebilir.'],
        ].map(([q,a])=>`<details class="help-question"><summary>${q}${icon('plus')}</summary><p>${a}</p></details>`).join('')}</section><aside class="panel contact-card"><div class="information-icon">${icon('help')}</div><h2>Firma desteği</h2><div class="document-status">Örnek iletişim bilgileri</div><dl class="contact-details"><dt>E-posta</dt><dd>${company.email}</dd><dt>Telefon</dt><dd>${company.phone}</dd><dt>Çalışma saatleri</dt><dd>${company.hours}</dd></dl><p>Talebinizde sipariş numaranızı ve ürün kodunu belirtmeniz yeterli.</p><p class="contact-note">Demo bilgileridir; bu kanallardan destek verilmez.</p><a class="button" href="#orders">Siparişlerime git</a></aside></div>`;
    }
    if (route === 'about') {
        return heading('Hakkımızda','U1 Business bayi sipariş portalı.') + `<section class="panel institutional-page"><div class="eyebrow">U1 BUSINESS</div><h2>Ürünlerinize ve siparişlerinize tek yerden ulaşın.</h2><p>U1 Business; otomotiv elektroniği, bağlantı ürünleri ve servis ekipmanları için ürün arama ve bayi sipariş süreçlerini bir araya getirir.</p><p>Katalogdan ürünleri inceleyebilir, sepetinizi oluşturabilir ve siparişlerinizin durumunu takip edebilirsiniz.</p><h3>Servislerin günlük ihtiyaçları için</h3><p>Diagnostik cihazlar, elektronik parçalar, servis ekipmanları ve bağlantı ürünlerinden oluşan katalog, işletmelerin ürün koduyla hızlıca sipariş hazırlayabilmesi için düzenlenmiştir.</p><h3>Firma bilgileri</h3><dl class="contact-details"><dt>Örnek firma unvanı</dt><dd>${company.name}</dd><dt>Adres</dt><dd>${company.address}</dd><dt>İletişim</dt><dd>${company.email} · ${company.phone}</dd></dl><div class="institutional-note">Bu, mini proje için hazırlanmış örnek bir firma profilidir.</div></section>`;
    }
    const doc = documents[route];
    if (doc) return heading(doc.title, doc.subtitle) + `<article class="panel institutional-page legal-document"><div class="document-status">Örnek metin · Demo proje</div><p class="document-intro">Bu içerik arayüzü örneklendirmek için hazırlanmıştır. Gerçek firma bilgileri ve ticari koşullar yayın öncesinde tamamlanmalıdır.</p>${doc.sections.map(([title, text], i) => `<section><h2><span>${String(i + 1).padStart(2, '0')}</span>${title}</h2><p>${text}</p></section>`).join('')}<div class="institutional-note">U1 Business · Örnek kurumsal içerik</div></article>`;
    return '';
}
