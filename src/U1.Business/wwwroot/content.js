// Demo copy: replace company details and obtain legal review before publication.
export const company = {
    name: 'U1 Otomotiv ve Servis Ekipmanları',
    email: 'destek@example.com',
    phone: '+90 (212) 000 00 00',
    address: 'Örnek Sanayi Sitesi, 1. Cadde No: 10, İstanbul',
    hours: 'Pazartesi–Cuma · 09.00–18.00',
};

export const documents = {
    returns: {
        title: 'İade şartları', subtitle: 'İade ve değişim başvuruları için izlenecek adımlar.',
        sections: [
            ['Başvuru', 'İade veya değişim talebinizi sipariş numarası, ürün kodu ve talep gerekçesiyle destek@example.com adresine iletin. Hasar veya yanlış ürün söz konusuysa ürünün ve ambalajın fotoğraflarını başvurunuza ekleyin.'],
            ['Ürünün kontrolü', 'Teslim aldığınız ürünün kodunu, adedini ve araç uyumluluğunu montajdan önce kontrol edin. Taşıma sırasında görülen hasarı teslimat görevlisine bildirin ve mümkünse tutanakla kayıt altına alın.'],
            ['Gönderim hazırlığı', 'Başvurunuz değerlendirildikten sonra paylaşılacak gönderim bilgilerini kullanın. Ürünü aksesuarları, varsa seri numarası bilgisi ve sağlam koruyucu ambalajıyla hazırlayın. İnceleme için gereken belgeler başvuru sırasında belirtilir.'],
            ['İnceleme ve sonuç', 'Ürünün durumu ve başvuru gerekçesine göre değişim, onarım veya bedel iadesi seçenekleri değerlendirilir. Gönderim masrafları ve varsa ödeme iadesinin yöntemi işlem öncesinde karşılıklı olarak netleştirilir. Portal üzerinden otomatik iade işlemi yapılmaz.'],
            ['Ticari alımlar', 'Bu portal bayi ve işletme siparişleri için tasarlanmıştır. Uygulanacak koşullar, tarafların sözleşmesine ve işlemin niteliğine göre belirlenir. Bu örnek metin, emredici mevzuattan doğan hakları sınırlandırmaz.'],
        ],
    },
    privacy: {
        title: 'Gizlilik politikası', subtitle: 'Hesap ve sipariş bilgilerinin kullanımına ilişkin açıklamalar.',
        sections: [
            ['Kapsam ve firma bilgileri', 'Bu sayfa U1 Business demo portalının veri kullanımını açıklar. Gerçek veri sorumlusunun unvanı, adresi ve başvuru kanalları yayına çıkmadan önce firma tarafından tamamlanmalıdır. Sayfadaki firma ve iletişim bilgileri örnektir.'],
            ['Portalda tutulan bilgiler', 'Hesap oluşturulurken ad, soyad, e-posta, telefon ve varsa firma bilgileri kaydedilir. Sepet, sipariş kalemleri, sipariş tarihleri ve durumları hesabınızla ilişkilendirilir. Şifreler açık metin olarak değil, şifre doğrulaması için üretilen hash değerleriyle saklanır.'],
            ['Kullanım amacı', 'Bu bilgiler hesabınıza giriş yapabilmeniz, ürün siparişi oluşturmanız ve siparişlerin firma tarafından yönetilebilmesi için kullanılır. Yetkili yöneticiler iş süreçleri kapsamında bayi ve sipariş kayıtlarına erişebilir.'],
            ['Oturum çerezleri', 'Portal, oturumunuzu tanımak ve isteklerin güvenliğini sağlamak için gerekli çerezleri kullanır. Bu demo sürümünde reklam veya ziyaretçi analitiği aracı bulunmaz. Kart bilgileri toplanmaz; online ödeme hizmeti bağlı değildir.'],
            ['Saklama ve paylaşım', 'Kayıtlar uygulamanın SQL Server veritabanında tutulur. Demo sürümünde kargo, banka veya pazarlama sistemlerine otomatik veri aktarımı bulunmaz. Gerçek kullanımda saklama süreleri, işleme şartları ve alıcı grupları firma tarafından belirlenerek ayrı bir aydınlatma metninde açıklanmalıdır.'],
            ['Bilgi ve düzeltme talepleri', 'Hesap bilgilerinizle ilgili talepler için firma yetkilisine başvurabilirsiniz. Örnek iletişim adresi: destek@example.com. Canlıya geçişte kimlik doğrulama ve başvuru değerlendirme süreci gerçek iletişim kanallarıyla birlikte yayınlanmalıdır.'],
        ],
    },
    terms: {
        title: 'Satış sözleşmesi', subtitle: 'Bayi sipariş sürecine ilişkin örnek ticari koşullar.',
        sections: [
            ['Taraflar ve kapsam', 'Bu örnek metin, U1 Otomotiv ve Servis Ekipmanları ile portalda hesabı bulunan bayi arasındaki ürün sipariş sürecini tanımlar. Ticari unvan, vergi bilgileri, tebligat adresleri ve yetkili kişiler gerçek sözleşmede ayrıca belirtilmelidir.'],
            ['Ürün bilgileri ve uyumluluk', 'Sipariş öncesinde ürün kodu, açıklama, adet ve birim fiyat kontrol edilmelidir. Araçla uyumluluk gerektiren ürünler için teknik teyit alınmalıdır. Demo katalogdaki fotoğraflar ürün türünü temsil eder; belirli bir marka veya modelin satış taahhüdü değildir.'],
            ['Siparişin oluşturulması', 'Sepetin onaylanmasıyla sipariş sisteme Bekliyor durumunda kaydedilir. Sipariş anındaki ürün ve fiyat bilgileri sipariş kaydında korunur. Firma siparişi değerlendirerek Onaylandı veya Reddedildi durumuna geçirir; durum Siparişler ekranından takip edilir.'],
            ['Fiyat ve ödeme', 'Fiyatlar Türk lirası olarak gösterilir. Vergi, teslimat masrafı, vade ve ödeme yöntemi gibi ticari koşullar işlemden önce taraflarca netleştirilmelidir. Bu demo portalı ödeme tahsil etmez; gösterilen örnek iletişim veya banka bilgilerine ödeme yapılmamalıdır.'],
            ['Teslimat ve kontrol', 'Teslimat adresi, taşıyıcı ve sevk tarihi firma ile bayi arasında belirlenir. Teslim alınan ürünler adet, kod ve görünür hasar açısından kontrol edilmeli; farklılıklar sipariş numarasıyla birlikte firmaya bildirilmelidir.'],
            ['İptal, iade ve iletişim', 'Sipariş değişikliği, iptal veya iade talepleri firma desteğine iletilir. Ürünün ve siparişin durumuna göre talep değerlendirilir. İade başvuru adımları İade şartları sayfasında açıklanır. Bu örnek metin gerçek sözleşmenin veya hukuki değerlendirmenin yerine geçmez.'],
        ],
    },
};
