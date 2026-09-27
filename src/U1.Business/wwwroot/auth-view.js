import { brand, field, icon } from './app-core.js?v=20260928a';

export function renderAuth(register = false) {
    const formType = register ? 'register' : 'login';

    const registrationFields = register
        ? `<div class="form-grid">
                ${field('Ad', 'firstName', '', 'text', 'required maxlength="80" autocomplete="given-name"')}
                ${field('Soyad', 'lastName', '', 'text', 'required maxlength="80" autocomplete="family-name"')}
           </div>
           ${field('Firma adı', 'company', '', 'text', 'maxlength="180" autocomplete="organization"')}
           ${field('Telefon', 'phone', '', 'tel', 'required maxlength="25" autocomplete="tel" placeholder="0532 123 45 67"')}`
        : '';

    const passwordAttributes = register
        ? 'required minlength="10" maxlength="128" autocomplete="new-password" placeholder="En az 10 karakter"'
        : 'required minlength="1" maxlength="128" autocomplete="current-password" placeholder="Şifrenizi girin"';

    return `<div class="auth">
        <section class="auth-art">
            ${brand()}
            <div class="auth-statement">
                <div class="eyebrow">PROFESYONELLER İÇİN TEDARİK</div>
                <h1>Bayi sipariş<br><em>platformu.</em></h1>
                <p>Otomotiv elektroniği ve servis ekipmanlarına ulaşmanın daha kolay yolu.</p>
            </div>
            <div class="auth-bottom">
                <div><b>01</b>Ürününü bul</div>
                <div><b>02</b>Siparişini oluştur</div>
                <div><b>03</b>İşine odaklan</div>
            </div>
        </section>

        <main class="auth-form-area" id="main" tabindex="-1">
            <form class="auth-form" data-form="${formType}">
                <div class="eyebrow">U1 BUSINESS'A HOŞ GELDİNİZ</div>
                <h2>${register ? 'Bayi hesabı oluşturun.' : 'Bayi girişi'}</h2>
                <p>${register ? 'Firma bilgilerinizi girerek hemen başlayın.' : 'Devam etmek için hesabınıza giriş yapın.'}</p>

                ${registrationFields}
                ${field('E-posta adresi', 'email', '', 'email', 'required maxlength="200" autocomplete="username" placeholder="ornek@firmaniz.com"')}
                ${field('Şifre', 'password', '', 'password', passwordAttributes)}

                <div id="auth-error" class="auth-error" role="alert"></div>
                <button class="button primary" type="submit">
                    ${register ? 'Hesap oluştur' : 'Giriş yap'} ${icon('arrow')}
                </button>

                <div class="auth-switch">
                    ${register ? 'Zaten hesabınız var mı?' : 'Henüz bayi hesabınız yok mu?'}
                    <a href="#${register ? 'login' : 'register'}">${register ? 'Giriş yapın' : 'Hesap oluşturun'}</a>
                </div>

                <div class="auth-demo">
                    U1 Business · Güvenli bayi erişimi<br>
                    Hesap erişimi için firma yöneticinizle iletişime geçin.
                </div>
            </form>
        </main>
    </div>`;
}
