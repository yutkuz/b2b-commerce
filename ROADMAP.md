# U1 Business geliştirme yol haritası

Son inceleme: **2 Ekim 2026** · Uygulama commitleri: **df3ab6b**, **64c6f42** · R24–R27 tamamlandı; uygulama GitHub CI başarılı.

## Sonuç

**Önceki dokuz aşama tamamlandı. R24–R27 uygulandı.** Yönetim özeti, bayi grubu kayıt tutarlılığı, mobil/yönetim kullanımı ve kesilen görsel temizliğini kurtarma doğrulandı. Uygulama GitHub CI başarılı; README ve 15 demo ekran görüntüsü yenilendi.

Kullanıcının 2 Ekim 2026 talimatıyla ROADMAP.md ve AGENTS.md GitHub deposunda sürümlenir. Her ajan kendi görevi/oturumu sonunda ilerleme ve doğrulama kaydını bu dosyada günceller. README ürün ve kurulum belgesi olarak kalır.

## Planın yönetimi

- Plan sahibi Astra / ana yönetici ajandır. Kapsam, öncelik, sıra ve kabul ölçütlerini o yönetir; kullanıcının açık talimatları önceliklidir.
- Uygulayıcı ajan yalnız atanmış işin kabul ölçütleri karşılandığında ilgili kutuyu işaretler; tarih, uygulama commit'i, test sonucu ve kalan sınırlamayı ekler. Kısmi veya doğrulanmamış işi tamamlandı saymaz.
- Yeni bulgular plan sahibine raporlanır. Her görev/oturum sonunda durum, tarih, değişiklik özeti, gerçek test sonucu, uygulama commit SHA ve varsa kalan adım kaydedilir; doğrulanmamış iş tamamlandı sayılmaz. Commit veya CI henüz yoksa açıkça bekliyor yazılır ve sonuç alındığında güncellenir.
- Her değişiklikte kullanıcının mevcut dosyaları korunur. Yetkilendirilmiş push öncesinde uzak dal ve ilgili testler kontrol edilir. Teknik, normal commit açıklamaları kullanılır.
- Bu kurallar çalışma düzenidir; GitHub dosya erişim kısıtlaması değildir.

## Güncel doğrulama

2 Ekim 2026 yerel doğrulama: Release çözüm derlemesi **0 uyarı/0 hata**, kaynak ve JavaScript denetimleri başarılı, **API 64/64**, **Chromium 16/16**. Son arayüz düzeltmesinden sonra ilgili kurtarma testi ayrıca **1/1** geçti. [64c6f42 GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/37005754556) başarılı: **64 API, 16 Chromium**, sıfır başarısız test. README ve 15 demo ekran görüntüsü ayrıca görsel olarak kontrol edildi; tarayıcı hata kaydı boş.

## Sonraki bakım sırası

### R24 · P2 · Yönetim özetini sipariş durumlarıyla uyumlu yap

- [x] AdminDashboardEndpoints.cs revenue sorgusu yalnız Onaylandı durumunu topluyor; Hazırlanıyor/Sevk edildi aşamasına geçen sipariş tutardan düşüyor. Demo ekranında bu siparişler varken kart 0 gösterdi. Onaylanan sipariş tutarı; Onaylandı, Hazırlanıyor, Sevk edildi, Teslim edildi durumlarını kapsasın; Bekliyor/ret/iptal hariç olsun. Tahsilat veya ciro iddiası ekleme.
- [x] Katalog ürün sayısı ve kritik stok listesinde arşiv ürünlerini hariç tut veya kapsamı açıkça etiketle; satıştaki ürün özeti arşivi içermesin.
- **Kabul:** durum geçişi ve arşivleme API testleri; kart açıklaması sonuçla uyumlu, sipariş ileri aşamaya geçince tutar azalmıyor.

- **İlerleme:** 2026-10-02 | durum: tamamlandı | commit: `df3ab6b` | test: DashboardSummaryTests durum geçişi/arşivleme testleri tam API 64/64 paketi içinde geçti; kart metni ve sorgu kapsamı incelendi. | sınırlama: tutar tahsilat değildir.

### R25 · P2 · Bayi grubu oluşturma ve audit kaydını birlikte kaydet

- [x] AdminPricingEndpoints.cs POST dealer-groups, grup ve audit için dış transaction olmadan iki SaveChanges çağırıyor. Audit hatası grubu kalıcı bırakabilir. İki kaydı tek transaction içine al.
- [x] Audit yazım hatasıyla rollback testi; grup ve olay birlikte oluşur veya ikisi de oluşmaz. Aynı ad çakışması 409 olarak kalır.
- **İlerleme:** 2026-10-02 | durum: tamamlandı | commit: `df3ab6b` | test: DealerGroupAuditTests gerçek SQL CHECK kısıtıyla audit yazım hatasını zorladı; grup rollback, başarılı audit ve aynı ad 409 doğrulandı. Tam API 64/64 başarılı. | sınırlama: SQL bağlantısının commit anında kesilmesi ayrıca simüle edilmedi.

### R26 · P3 · Yönetim dili ve mobil kullanım

- [x] Geçmiş ekranındaki DealerGroupCreated/DealerGroupAssigned gibi teknik olay adlarını Türkçe göster; API kodları sabit kalsın.
- [x] Fiyat grubu oluşturma/değiştirme/atama ve eski form çakışması için tarayıcı senaryosu ekle.
- [x] Mobil sepette yatay kaydırılan tablonun toplam/çıkarma kontrollerine erişimi belirginleştir; kart düzeni veya kaydırma ipucu değerlendir.
- **Kabul:** klavye ve mobil sipariş akışı korunur; anlaşılır etiketler ve tarayıcı testleri. Mevcut mobil toplam/onay düğmesi çalışıyor; bu kullanım iyileştirmesidir.
- **Uygulayıcı ilerlemesi:** 2026-10-02 | commit: `64c6f42`; durum: tamamlandı | uygulama: geçmiş olayları Türkçeleştirildi; bayi grubu ve kullanıcı eski formunda taslak korunup güncel veri açık eylemle yükleniyor; mobil sepet kartları toplam/çıkarma kontrollerini görünür tutuyor. | doğrulama: Release Chromium paketi 16/16 başarılı (`.artifacts/r24-r27-browser/browser.trx`); grup oluşturma/güncelleme/atama, iki eski form çakışması, Türkçe geçmiş filtreleri, 390 px mobil sepet, yatay taşma kontrolü, klavyeyle çıkarma ve sipariş oluşturma geçti. Kaynak/import kontrolü 78 dosyada başarılı. R27 arayüzü ek kontrolü 1/1 başarılı (`.artifacts/r27-browser-followup/recovery.trx`): kısmi temizliğin bekleyen işlem ekranına yönlendirmesi, gerçek auditsiz pending dosyasının klavyeyle geri koyulması ve bayt eşitliği, sonuç audit uyarısının işlem kimliğiyle görünmesi. | sınırlama: Chromium dışı motorlar ölçülmedi; kısmi cleanup ve auditWarning yanıtları arayüz testinde simüle edildi, gerçek dosya/audit hata kanıtları R27 API testlerinde; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/37005754556) başarılı.

### R27 · P3 / ihtiyaç halinde · Kesilen görsel temizliğini kurtarma

- [x] Bekleyen .pending-cleanup işlemleri için durum/uzlaştırma ekranı ve kontrollü geri koyma/sonlandırma değerlendir. Audit ve ürün referanslarını doğrulamadan otomatik silme yapma.
- **Kabul:** dosya taşıma ile audit commit arasında kapanmada güvenli kurtarma yolu; kullanılan dosya korunur. Mevcut manuel kurtarma belgesi geçerlidir.
- **Uygulayıcı ilerlemesi:** 2026-10-02 | commit: `64c6f42`; durum: tamamlandı | uygulama: bekleyen işlem önizlemesi, referans/audit kontrolü ve SQL kilidiyle geri koyma/sonlandırma mevcut; dosya işlemi sonrası sonuç audit hatası işlem kimliği ve açık uyarıyla başarılı dosya sonucunu bildirir, kalıcı Requested kaydı korunur. | doğrulama: ana ajanın Release derlemesi 0 uyarı/0 hata; tam API paketi 64/64 başarılı (`.artifacts/r24-r27-api-final/api.trx`). R27 kapsamındaki ikinci audit yazımı SQL CHECK kısıtıyla zorlanan restore/finalize ve iki dosyadan biri taşınmış gerçek disk durumundan devam testleri bu pakette geçti. | sınırlama: Chromium 16/16 ve son kurtarma testi 1/1 geçti; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/37005754556) başarılı. SQL ve dosya sistemi tek atomik işlem değildir. Sonuç audit hatasında kalıcı istek kaydı ve işlem kimliğiyle kontrol gerekir; otomatik sonuç kaydı yeniden oluşturulmaz.

1 Ekim 2026 kullanıcı talebiyle R24–R27, GPT-6.1 Sol alt ajanlarına atandı. R24/R25 backend ve API testleri, R26 arayüz ve tarayıcı testleri, R27 kesilen görsel temizliği kurtarma olarak paylaştırıldı. Ana ajan bütünleştirme, doğrulama, commit/push ve bu belgedeki kapanış kanıtlarını yönetir. Üretim/ERP/ödeme ve koşullu R22 kapsam dışında.

## Tamamlanan dokuz aşamanın sırası (geçmiş)


1. **R23:** kategori yazmalarında eşzamanlılık ve kontrollü hata yanıtı.
2. **R10 kalan doğrulamalar:** arşivleme/checkout yarışı ve kategori/arşivleme tarayıcı akışları.
3. **R13:** sipariş durumları, ret nedeni ve yönetici notu, yazdırılabilir form, yeniden sepete ekleme.
4. **R17:** son yöneticiyi koruyarak yönetici pasifleştirme.
5. **R18:** kullanıcı bazlı ölçülü hız sınırları.
6. **R19:** görsel depolama kotası ve önizlemeli güvenli temizlik.
7. **R12:** bayinin kendi profilini düzenlemesi.
8. **R20:** tek bayi grubu iskontasıyla sınırlı fiyatlandırma.
9. **R21:** önizlemeli ve sınırlı CSV aktarımı.

Sol bu sırayı uygular. Aşağıdaki eski numaralı başlıklar görev ayrıntılarını korur; yürütme sırası bu listedir. Tamamlanan R16/R11 ve geçmiş işler tekrar yapılmaz. R22, R14, R15 ve düşük öncelikli/şimdilik alınmayan öneriler otomatik başlatılmaz.

Her aşamada ilgili API ve gerekiyorsa gerçek SQL eşzamanlılık/tarayıcı testleri, Release derlemesi, kaynak ve JavaScript kontrolleri çalıştırılır. Şema değişirse eski verilerin korunması ve tekrar başlangıç doğrulanır. Başarılı aşama normal teknik commit açıklamasıyla pushlanır; uzak dal önce yeniden kontrol edilir, force push yapılmaz. O commit'in CI sonucu başarılı olmadan sonraki aşama tamamlandı sayılmaz. Arıza varsa düzeltilir; ilgisiz mevcut kullanıcı değişikliği commit'e katılmaz.

ROADMAP.md ve AGENTS.md pushlanır; docs/, yerel ayarlar, test çıktıları ve kullanıcı dosyası `main` pushlanmaz. README yalnız kullanıcıya yönelik kurulum/özellik açıklamalarıyla güncellenir. Her aşamanın commit'i, test sonucu, CI bağlantısı ve sınırlaması burada kaydedilir. Sonunda masaüstü HEAD ile origin/main eşitliği doğrulanır. Görevler tamamlanmış gibi işaretlenmeden kalıcı bir engel varsa açıkça raporlanır.

### R23 · Öncelik 1 · Kategori yazmalarında eşzamanlılık

- [x] Kategori oluşturma, yeniden adlandırma ve birleştirmede aynı kayıt ve ters yönlü birleştirme yarışlarını izole SQL testleriyle incele. Kategori birleştirme ile ürün düzenleme/arşivleme etkileşimini de kontrol et.
- [x] Gerekliyse sabit kilit sırası ve sınırlı yeniden deneme ekle. Her denemede transaction ve izlenen EF durumu temiz başlasın; yalnız geçici SQL hataları yeniden denensin.
- [x] Son denemede 500 yerine anlaşılır, makinece ayırt edilen tekrar denenebilir yanıt ver; eski rowversion ve ad çakışmaları 409 davranışını korusun.
- [x] Rollback'te yarım kategori taşıması, ürün kaybı veya mükerrer geçmiş kaydı oluşmadığını; başarılı ve tükenen denemeleri doğrula. Test yalnız gecikme/sleep şansına dayanmasın.
- **Kabul:** eşzamanlı işlemler geçerli tek sonuç veya kontrollü çakışma/yeniden deneme yanıtıyla biter; veri ve audit tutarlılığı korunur. Gerçek yarış testi ile enjekte edilen hata testi raporda ayrı belirtilir.
- **Bağımlılık:** mevcut R11 ve R10 altyapısı. Başlangıç bulgusu olasılıktır; testte doğrulanmayan hata gerçekleşmiş gibi yazılmaz.
- **Tamamlanma:** 2026-09-30 | commit: `2952f27` | test: Release derlemesi 0 uyarı/0 hata; kaynak ve JavaScript kontrolleri başarılı; API 32/32; Chromium 10/10; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36762731396) başarılı. Gerçek SQL kilidiyle oluşturma, yeniden adlandırma, ters yönlü birleştirme ve birleştirme/ürün düzenleme-arşivleme yarışı; ayrıca zorlanan SQL komut zaman aşımında başarılı ve tükenen yeniden denemeler doğrulandı. | sınırlama: SQL deadlock 1205 doğrudan zorlanmadı; geçici hata testi gerçek komut zaman aşımı -2 üzerindendir.

## Yeni çalışma sırası

Yukarıdan aşağı ilerlenir. R numaraları kalıcı görev kimliğidir; numaralarının büyüklüğü öncelik değildir. Önceki R10–R13 maddeleri burada genişletildi; aynı işi ikinci kez açmadık. P1 doğrulanmış bakım kusuru, P2 sonraki ürün/bakım adımları, P3 sonraya bırakılan kapsam anlamındadır. Bir madde tamamlanmadan ona bağımlı özellik başlatılmaz.

### 1. R16 · P1 · Geliştirme denetimini ve depo açıklamasını düzelt

- [x] `tests/verify-source.ps1` taramasından `bin`, `obj` ve diğer üretilen çıktıları dizin bazında dışla; gerçek kaynak denetimleri çalışmaya devam etsin.
- [x] GitHub depo açıklamasını Dapper yerine EF Core kullanılan mevcut mimariyle uyumlu hale getir.
- **Neden ilk:** küçük ve doğrulanmış iki bakım sorunu. 29 Eylül kontrolünde kaynak denetimi `obj/Release/net10.0/SelfRegisteredExtensions.cs` için satır sonu hatası verdi; GitHub açıklamasında Dapper hâlâ mevcut.
- **Kabul:** temiz checkout'ta ve Release derlemesinden sonra denetim geçer; gerçek kaynak dosyasına eklenen geçersiz boşluk hâlâ yakalanır. Açıklama güncellenir, uygulama davranışı değişmez.
- **Bağımlılık:** yok. R07'nin tamamlanan dosya ayrımı yeniden yapılmaz.
- **Tamamlanma:** 2026-09-29 | commit: `5b9e3af` | test: Release çözüm derlemesi 0 uyarı/0 hata; derleme sonrası kaynak denetimi 48 dosyada başarılı; geçici gerçek kaynak sekmesi beklendiği gibi reddedildi; GitHub açıklaması API üzerinden doğrulandı | sınırlama: depo açıklaması Git commit'inin parçası değildir.

### 2. R11 · P2 · Stok hareketleri ve yönetici işlem geçmişi

- [x] Stok düzeltme, sipariş düşümü ve red iadesini; miktar farkı, önceki/yeni stok, zaman, yapan kullanıcı, neden ve ilgili siparişle kaydet.
- [x] Fiyat değişikliği, kullanıcı aktifliği/parola sıfırlama olayı, duyuru ve sipariş durumu için gerekli yönetici olaylarını kaydet. Parola/hash, cookie, token ve gereksiz kişisel veriler kayda girmez.
- [x] Yalnız yöneticiye açık, tarih/ürün/kullanıcı/işlem filtresi ve sayfalaması olan salt okunur geçmiş ekranı ekle.
- **Tasarım kararı:** küçük, açık veri modelleri kullan; stok hareketiyle genel yönetici olayını ilişkilendir. Her veritabanı değişikliğini otomatik kopyalayan geniş bir denetim çatısı veya event-sourcing sistemi kurma.
- **Kabul:** başarılı işlem ve kaydı aynı transaction'da kalıcılaşır; rollback'te ikisi de geri alınır. Aynı sipariş/ret tekrarı ikinci hareket üretmez. Eski stok, geçiş anında başlangıç bakiyesi olarak kaydedilir; geçmişe ait bilinmeyen hareketler uydurulmaz. Yeni migration eski siparişleri korur.
- **Bağımlılık:** R16. Bu, ilk büyük özellik çalışmasıdır.
- **Tamamlanma:** 2026-09-29 | commit: `2a606c7` | test: Release çözüm derlemesi 0 uyarı/0 hata; kaynak denetimi 52 dosyada başarılı; API 24/24; Chromium 10/10; V1→V5 yükseltme ve rollback/idempotency kontrolleri başarılı | sınırlama: V5 öncesi hareketler yalnız geçiş bakiyesiyle temsil edilir; geçmiş aynı veritabanında salt okunur uygulama ekranıdır, harici değiştirilemez kayıt deposu değildir.

### 3. R10 · P2 · Kategori yönetimi ve ürün arşivleme

- [x] Kategori ekle/düzenle; kullanılan kategoride ürünleri kontrollü taşı veya kategorileri birleştir.
- [x] Ürünü fiziksel silmeden arşivle/geri aç; neden, zaman ve işlemi yapan yöneticiyi kaydet.
- [x] Katalog, arama, marka/kategori sayımları ve yeni siparişlerde arşiv ürünlerini dışla; mevcut sepette açıklayıcı uyarı ve çıkarma seçeneği göster.
- **Temel özellik kanıtı:** `e59b61b`; CatalogManagementTests sıralı oluşturma/yeniden adlandırma/birleştirme/arşivleme/geri açma, eski sürüm çakışması, yetki, sepet ve geçmiş sipariş kontrolleri; CI 25 API ve 10 tarayıcı testi başarılı. Tam R10 kapanışı değildir.
- [x] Gerçek eşzamanlı arşivleme/checkout testini iki kilit sırası için ekle. Arşivleme önce kesinleşirse yeni sipariş reddedilsin; checkout önce kesinleşirse geçerli tek sipariş korunsun. Stok, sepet, snapshot ve geçmiş tutarlılığını doğrula.
- [x] Tarayıcıda kategori ekleme/yeniden adlandırma/birleştirme, ürün arşivleme/geri açma ve bayinin arşiv ürünlü sepetten kurtulmasını test et; eski yönetici formundaki anlaşılır hata ve girdilerin korunmasını doğrula.
- **Kabul:** arşivleme ve checkout tutarlı bir işlem sırasına oturur; eski sipariş kalemleri/fiyatları değişmez. Geri açma ürünü yeniden satışa sunar. İki yönetici değişikliği birbirini sessizce ezmez; yetki, sepet, sayım, geçmiş sipariş ve yeni tarayıcı testleri geçer.
- **Bağımlılık:** R11'in kayıt altyapısı.
- **Tamamlanma:** 2026-09-30 | commit: `455686d` (temel özellikler `e59b61b`) | test: Release derlemesi 0 uyarı/0 hata; kaynak kontrolü; API 34/34; Chromium 11/11; iki SQL kilit sırası üç ek tekrarda başarılı; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36764883432) başarılı. | sınırlama: gerçek SQL yarışında checkout-önce akışında 1205 deadlock yeniden üretildi ve ürün satırı için sabit UPDLOCK sırasıyla giderildi; sonraki aşamalardaki sipariş durum/çıktı işlevleri bu kapsamda değildir.

### 4. R13 · P2 · Sipariş operasyonu ve çıktı

- [x] Durum geçişlerini açık tabloyla tanımla: Bekliyor, Onaylandı, Hazırlanıyor, Sevk edildi, Teslim edildi; red/iptalin hangi aşamaya kadar mümkün olduğunu belirt.
- [x] Durum geçmişi, yönetici notu ve zorunlu ret nedeni ekle. Yöneticiye özel notu bayi yanıtlarına dahil etme.
- [x] Önce yazdırılabilir sipariş formu ekle; tarayıcının PDF'ye kaydetmesi yeterlidir. Ayrı PDF üretim servisi şu aşamada gerekli değil.
- [x] Eski siparişten sepete eklemeyi güncel ürün, fiyat, stok ve satış durumu kontrolüyle sun; sessizce otomatik yeni sipariş oluşturma.
- **Kabul:** geçersiz/eski form geçişleri reddedilir; tekrarlanan istek mükerrer stok iadesi yapmaz. Sevk edilmiş siparişe doğrudan ret ile stok iadesi yapılmaz; ayrı iade süreci bu aşamanın dışında kalır. Sipariş çıktısı kayıt anındaki snapshot'ı kullanır.
- **Bağımlılık:** R11 ve R10.
- **Tamamlanma:** 2026-09-30 | commit: `f806530` | test: Release derlemesi 0 uyarı/0 hata; kaynak ve JavaScript kontrolleri başarılı; API 37/37; Chromium 12/12; V1→V7 şema yükseltmesi ve tekrar başlangıç doğrulandı; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36768208636) başarılı. | sınırlama: yazdırma Chromium tarayıcı/PDF kaydetme akışıyla doğrulandı; ayrı PDF servisi ve sevk sonrası iade süreci bu kapsamda değildir.

### 5. R17 · P2 · Yönetici hesabını güvenle pasifleştirme

- [x] Mevcut “hiçbir Admin pasifleştirilemez” kuralını, başka aktif yönetici varsa hedef yöneticiyi kapatabilecek şekilde düzenle.
- [x] Son aktif yöneticiyi kapatmayı transaction içinde engelle; eşzamanlı iki pasifleştirme de bu kuralı korusun. Kullanıcının kendi hesabını bu ekrandan kapatmasını engelle.
- **Kabul:** yetkili ikinci yönetici hedef hesabı kapatabilir; hedefin mevcut oturumu AuthVersion ile sonraki istekte reddedilir. Son yönetici her koşulda aktif kalır ve işlem R11 geçmişine yazılır. İki yöneticili test verisi kullanılır; projeye varsayılan ikinci yönetici veya genel rol-yükseltme endpoint'i eklenmez.
- **Bağımlılık:** R11. MFA bu işin kapsamı değildir.
- **Tamamlanma:** 2026-09-30 | commit: `63600b9`, arayüz yenileme `1570f79` | test: Release derlemesi 0 uyarı/0 hata; kaynak ve JavaScript kontrolleri başarılı; API 38/38, Chromium 12/12; iki yöneticili SQL yarış testi üç ek tekrarda başarılı; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36770076888) başarılı. | sınırlama: son yönetici kontrolü uygulama üzerinden yapılan pasifleştirmeleri kapsar; veritabanına uygulama dışından doğrudan yapılan yönetici değişiklikleri bu kilide katılmaz. MFA kapsam dışıdır.

### 6. R18 · P2 · Maliyetli API işlemlerine ölçülü hız sınırı

- [x] Katalog araması, sepet/sipariş yazmaları ve görsel yükleme için ayrı maliyetlere uygun politikalar tanımla; kullanıcı başına sınırla, bütün bayileri ortak tek kotaya sıkıştırma.
- [x] Kullanıcı kimliğini kullanan politikalar için middleware sırasını doğrula; giriş/kayıt politikası anonim kullanıcılar için korunmalı.
- **Tamamlanma:** 2026-10-01 yönetici incelemesi | commit: `471bcc0`, `716e47d` | kanıt: Program.cs kullanıcı bölümü ve middleware sırası; RateLimitTests 429/Retry-After ve ikinci kullanıcının bağımsız kotası; `c661910` CI 43 API/13 Chromium başarılı | sınırlama: tek süreçli kota; sepet ve sipariş yazmaları ortak kullanıcı kotası kullanır, yerel demo için kabul edildi.
- **Kabul:** aşımda anlaşılır 429 ve uygun tekrar süresi verilir. Normal arama ve mevcut eşzamanlı sipariş testleri çalışır; bir bayi diğerinin kotasını tüketmez. Checkout belirsiz yanıt kurtarması aynı requestId'yi korur. Sınırlar yapılandırılabilir ve test edilebilir olur.
- **Bağımlılık:** R13; bunun yerel demoda doğrulanmış saldırı değil, dayanıklılık iyileştirmesi olduğu kabul edilir. Dağıtık kota altyapısı eklenmez.

### 7. R19 · P2 · Görsel depolama kotası ve güvenli temizlik

- [x] Mevcut biçim/piksel/dosya boyutu kontrollerine toplam yükleme alanı kotası ve yeterli boş alan kontrolü ekle; eşzamanlı yüklemelerin kotayı birlikte aşmasını engelle.
- [x] Kullanılmayan görselleri önce listeleyen bir önizleme ekle. Ürün kaydı henüz yapılmamış yeni yüklemeler için bekleme süresi uygula; kullanılan dosyalar korunur.
- **Başlangıç kanıtı:** `943594e`–`6c2a4c2` değişiklikleri; ProductImageStorage, yönetici temizleme ekranı, ImageStorageTests ve eşzamanlı yükleme kotası/bekleme süresi/yol güvenliği testleri. Bu aşamada ürün referansı/temizlik yarışı ve audit başarısızlığı henüz açık kalmıştı.
- **Kabul:** kota doluyken hiçbir kısmi dosya kalmaz. Silme yalnız uploads içindeki kanonik, doğrulanmış yollarla ve açık seçimle yapılır; yol geçişi, bağlantı/reparse-point kaçışı ve referans verilen dosya silinmesi engellenir. Yedek/geri alma politikası belgelenir; temizlik R11 kaydına girer.
- **Bağımlılık:** R11, R18. Kullanıcı kotası ve otomatik zamanlanmış silme başlangıç kapsamı değildir; toplam kota ve denetimli temizlik yeterlidir.
- **Tamamlanma:** 2026-10-01 | commit: `f61e9c6`, CSV koordinasyonu `f01fbba` | test: Release derlemesi 0 uyarı/0 hata; kaynak/JavaScript kontrolleri; API 53/53; Chromium 14/14; iki SQL kilit sıralı ürün/temizlik yarışı, CSV/temizlik yarışı ve zorlanan audit kaydı hatasında dosya geri koyma doğrulandı; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36858240008) başarılı. | sınırlama: işlem ortasında süreç kapanırsa `.pending-cleanup` klasöründeki dosyalar işlem kimliği ve yedekle elle uzlaştırılır; otomatik zamanlanmış silme ve çok süreçli kota paylaşımı kapsam dışıdır.

### 8. R20 · P3 · Sınırlı B2B fiyatlandırma

- [x] İlk sürümde bayi grubu başına tek yüzde iskonto kullan; varsayılan yüzde 0 olsun. Ürün fiyatına iskonto bir kez uygulanır, sonuç iki ondalığa AwayFromZero ile yuvarlanır; birden çok kampanya/fiyat listesi eklenmez. Grup atama ve oran değiştirme yalnız yöneticide olur ve geçmişe yazılır.
- [x] Katalog, sepet ve checkout aynı sunucu fiyat hesabını kullansın. Sipariş kalemine liste fiyatı, uygulanan fiyat ve iskonto snapshot'ını ekle.
- **Tamamlanma:** 2026-10-01 yönetici incelemesi | commit: `648f0bb`, `3d47e5a`, son düzeltme `c661910` | kanıt: ortak Pricing hesabı, SQL fiyat snapshot alanları, yönetici grup ekranı ve PricingTests; iskonto değişince yeniden onay, yuvarlama, ayrı bayi fiyatı ve audit kontrolleri başarılı CI içinde | sınırlama: fiyat grubu yönetim ekranına özel tarayıcı senaryosu yok; API kanıtı mevcut, yerelde bu tur yeniden çalıştırılmadı.
- **Kabul:** istemcinin gönderdiği fiyat yetki kaynağı olmaz; fiyat/grup değişince yeniden onay gerekir. Eski siparişler yeniden fiyatlanmaz. Başka grubun fiyatına erişim ve yuvarlama/snapshot testleri geçer.
- **Bağımlılık:** R11, R13. Bayiye özel çoklu istisna, kampanya takvimi, minimum adet ve paket katı ancak temel fiyat akışı tamamlanıp iş kuralı seçilirse ayrı alt görev olur.

### 9. R21 · P3 · Önizlemeli toplu ürün aktarımı

- **Başlangıç kanıtı:** `origin/automation-r21-csv` / `0e20dcf`; [dal CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36785187222) başarılı. CSV API, belge ve üç API testi bu daldan alındı; sonraki commit'te arayüz, anlamlı önizleme ve yarış kontrolleri tamamlandı.

- [x] Önce tek, belgeli CSV şablonuyla içe/dışa aktarma; ardından satır bazlı hata raporu ve kayıt öncesi değişiklik önizlemesi ekle. Excel desteğini ihtiyaç oluşursa ekle.
- [x] Dosya/satır sınırı, mükerrer ürün kodu, kategori, para/adet biçimi ve stok düzeltme nedenini doğrula. Önizlemeden sonra değişmiş ürünleri rowversion ile yakala.
- **Kabul:** seçilen sınırlı batch tamamen uygulanır veya geri alınır; hatalı satırlar sessizce atlanmaz. Tekrar gönderilen aktarım ikinci stok hareketi üretmez. Dışa aktarılan hücrelerde formül enjeksiyonu engellenir; aktarımın kimliği ve değişiklikleri R11'de izlenir.
- **Bağımlılık:** R10, R11; fiyat listesi aktarımı istenecekse R20. Sonraki gerçek siparişleri geriye saran genel “import'u geri al” düğmesi eklenmez; gerekiyorsa yeni denetlenebilir düzeltme işlemi yapılır.
- **Tamamlanma:** 2026-10-01 | commit: dal aktarımı `44c2e97`, son düzenleme `f01fbba` | test: Release derlemesi 0 uyarı/0 hata; kaynak/JavaScript kontrolleri; API 53/53; Chromium 14/14; ters ürün sıralı eşzamanlı import üç ek tekrarda tek geçerli sonuç, farklı içerikte importId çakışması, eski/yeni değer önizlemesi, 1 MiB/1000 satır sınırı, CSV/görsel temizleme yarışı ve ekran üzerinden seç/önizle/uygula/dışa aktar doğrulandı; [GitHub CI](https://github.com/yutkuz/b2b-commerce/actions/runs/36858240008) başarılı. | sınırlama: yalnız belgeli CSV şablonu desteklenir; Excel ve sonraki işlemleri tersine çeviren genel geri alma düğmesi kapsam dışıdır.

### 10. R12 · P3 · Bayinin kendi profilini düzenlemesi

- [x] Önce kişi/firma/telefon bilgilerini kendi hesabından düzenleme ekle; e-posta/rol/aktiflik gibi yetki veya kimlik alanları bu formdan serbestçe değişmesin.
- **Tamamlanma:** 2026-10-01 yönetici incelemesi | commit: `ce116d3` | kanıt: ProfileTests kendi profilini düzenleme, hassas alanları değiştirememe, rowversion çakışması ve audit; Account_profile_stale_form_keeps_draft_and_reloads_remote_value tarayıcı testi; güncel CI 43 API/13 Chromium başarılı | sınırlama: e-posta/parola kurtarma halen kapsam dışı.
- **Kabul:** başka hesaba erişilemez; geçersiz bilgiler reddedilir; eski form çakışması veri kaybına yol açmaz. Gerekli işlemler geçmişe yazılır.
- **Bağımlılık:** R11. E-posta servisi olmayan yerel demo için parola kurtarma/e-posta doğrulama akışı şimdilik ertelendi. İleride gerçek kurtarma kanalı seçilirse süreli tek kullanımlık token ve oturum iptali ayrı kapsam olur.

### 11. R22 · P3 / koşullu · Bayi başvurusu veya davet

- [ ] Yalnız ürün ihtiyacı seçilirse mevcut açık kayıt yerine davet veya onay bekleyen başvuru modelinden birini uygula; ikisini birden kurma.
- **Kabul:** bekleyen/reddedilen hesap fiyat ve sipariş API'lerine erişemez; onay/red geçmişi tutulur; eski demo hesaplarının geçişi açıkça tanımlanır. Davet seçilirse süreli ve tek kullanımlık olur.
- **Bağımlılık:** R11; kullanıcı bu iş kuralını geliştirmeye almak isterse başlatılır. Vergi/firma doğrulama servisi ve gerçek kişisel veri toplama eklenmez.
- **Karar:** kayıt olan kullanıcının Dealer olması mevcut yerel/demo iş kuralıdır. Tek başına yetki atlama açığı veya acil yüksek risk olarak sınıflandırılmadı. Proje internete açılmayacağı için ilk üç iş arasına alınmadı.

## Düşük öncelikli / şimdilik alınmayan öneriler

- **Paket bakımı:** rapordaki SkiaSharp sürüm önerisi otomatik kabul edilmedi. Bakım yapılacağı gün resmi sürüm notları, güvenlik kayıtları ve uyumluluk kontrol edilir; sürüm yalnız daha yeni olduğu için yükseltilmez. Bu tur bağımlılık güvenlik taraması yeniden yapılmadı.
- **CI action SHA sabitleme:** istenirse düşük maliyetli bakım; güvenilir upstream SHA ve güncelleme mekanizması birlikte ele alınmalı. Şu an çalışan CI'ı değiştiren zorunlu görev değil.
- **CSP inline stil temizliği:** mevcut işlevlerin önüne alınmadı. Yapılacaksa dinamik grid genişlikleri ve modal stilleri tarayıcı testleriyle korunmalı.
- **Harici HTTPS ürün görselleri:** yönetici kontrollü mevcut özellik. Sıkılaştırma istenirse yerel yüklemeyle sınırla veya izinli kaynak listesi seç. Genel sunucu taraflı görsel indirme proxy'si ekleme; SSRF ve kaynak tüketimi yüzeyi oluşturur.
- **Kayıtta hesap varlığının anlaşılması:** yerel demo için düşük öncelik. Onay/davet akışı tek başına bunu çözmüş sayılmaz; kayıt ve hata yanıtları ayrıca tasarlanmalı.
- **MFA ve Development demo parolasıyla dışarı açılma önlemleri:** üretim yayını R14 ile birlikte kapsam dışında. Mevcut demo hesapları bilinçli geliştirme özelliği olarak korunur.
- **Performans ölçümü R15:** yalnız yavaşlık veya büyük katalog ihtiyacı oluşursa. Sırf bu liste için cache, mikroservis veya ayrı altyapı eklenmez.

## Tamamlanan teknik işler — geçmiş kanıtlar

- [x] **R00 · Masaüstünü güncel sürümle eşitle.**
  - Son doğrulama: 2026-09-29 | sürüm: `e4097a8`.
  - Önceki yarım test yaşam döngüsü değişiklikleri Git stash ve `docs/local-backups/before-roadmap-review-20260929-185602` altında korundu. Eski `tests/TestDatabaseCleanup.cs` aynı yedeğe taşındı; güncel `TestDatabaseLifecycle.cs` ile karışması önlendi.
  - İzlenmeyen kullanıcı dosyası `main` korundu. ROADMAP.md ve AGENTS.md o tarihte yereldi; 2 Ekim talimatıyla Git takibine alındı.

- [x] **R01 · SQL uygulama kilitlerinin gerçek dönüş kodunu kontrol et.**
  - Kanıt: `49f3133`, `4346623`, `5130083`; `Data/SqlApplicationLock.cs`.
  - `EXEC @result = sys.sp_getapplock; SELECT @result` kullanılıyor; boş sonuç başarı sayılmıyor. Komut süresi kilit bekleme süresinden uzun tutuluyor.
  - Sipariş, veritabanı oluşturma ve şema kilidi zaman aşımı testleri; stok/sepet/sipariş ve şema durumu kontrolleri mevcut. Test oturum kilitleri açıkça serbest bırakılıyor.
  - Doğrulama: güncel 24 API testi içinde başarılı.

- [x] **R02 · Eşzamanlılık ve hata sonrası yeniden denemeyi doğrula.**
  - Kanıt: `c5c6205`, `4878711`; `OrderResilienceTests.cs` ve kayıp yanıt tarayıcı testi.
  - Checkout ve durum değişikliğinde üç deneme sonrası kontrollü 503; rollback, aynı istek anahtarıyla tekrar, farklı bayilerin anahtar çakışması, son stok yarışı, çok ürün iadesi ve red/checkout yarışı doğrulanıyor.
  - Tarayıcı ilk sipariş yanıtını sunucu işlemi tamamlandıktan sonra kaybediyor; yeniden yüklemede aynı requestId ile tek sipariş kaldığı doğrulanıyor. Belirsiz sonuç penceresi önceki isteği sorgulamaya geçiyor.
  - Sınırlama: deterministik SQL hata enjeksiyonu `-2` komut zaman aşımıdır; `1205` deadlock doğrudan zorlanmadı. Başarılı commit sonrası istemci yanıt kaybı kapsanıyor; bütün ağ/SQL commit arızaları simüle edilmiş değildir.
  - Doğrulama: güncel API ve Chromium suite'leri başarılı.

- [x] **R03 · Eski şemadan yükseltmeyi ve EF/SQL eşleşmesini doğrula.**
  - Kanıt: `cc3d423`; güncel `SchemaUpgradeTests.cs`.
  - V1 şemasından güncel V4'e geçişte kullanıcı/parola hash'i, stok ve sipariş kalemleri korunuyor. İkinci başlangıç, daha yeni şemanın reddi ve temel tür/uzunluk/decimal/default/rowversion/ilişki eşleşmeleri test ediliyor.
  - Siparişin SQL varsayılan durumu EF eşlemesine yansıtıldı. SQL-first şema yönetimi korundu.
  - Doğrulama: güncel API suite'i başarılı. Bu test temel eşlemeleri kontrol eder; her kolon ve indeksin eksiksiz şema karşılaştırması değildir.

- [x] **R04 · Test veritabanlarının yaşam döngüsünü tamamla.**
  - Kanıt: `d47886a`, `3a38510`; `TestDatabaseLifecycle.cs`, API fixture, browser uygulama kapanışı ve yükseltme testi.
  - Tam üretilmiş DB adı üzerinden temizlik, isim sınırı kontrolü ve iptalden bağımsız kapanış temizliği var. Prefix'e göre toplu silme yapılmıyor. `U1_KEEP_TEST_DATABASES=1` ile koruma seçeneği README'de belgeli.
  - Testler bir DB temizlenirken ikinci DB'nin kaldığını ve varsayılan `U1Business` adının reddedildiğini doğruluyor.
  - Doğrulama: güncel API/tarayıcı CI başarılı. Bu incelemede iki ayrı test süreci eşzamanlı çalıştırılmadı; koruma seçeneği ayrıca çalıştırılmadı. `ClearAllPools` test sürecindeki tüm havuzları etkiliyor; ileride aynı süreçte paralellik açılırsa yalnız ilgili havuzu temizleme değerlendirilmeli.

- [x] **R05 · CI uyarıları ve hata teşhis çıktıları.**
  - Kanıt: `5269612`; `.github/workflows/build.yml`, test proje dosyaları ve browser tanılama kodu.
  - İptal belirteçleri eklendi; test projelerinde `TreatWarningsAsErrors` açık. TRX raporları ve başarısız browser senaryolarının screenshot/trace/server log çıktıları artifact olarak saklanıyor.
  - Doğrulama: güncel CI'da üç proje de 0 uyarı/0 hata. Artefact'lar yalnız izole demo test akışları için tasarlanmış; gerçek hesaplarla test veya hassas veri loglaması bu kapsamda değildir.

- [x] **R06 · Kritik yönetim ve hata akışlarını tarayıcıda koru.**
  - Kanıt: `138e079`, `be07cc5`, `24e3f83`, `8e031f1`, `fce267f`, `e4097a8`.
  - 10 senaryo: temel checkout; kayıp sipariş yanıtından kurtarma; admin onay/red durumunun bayi ekranında görünmesi; eski ürün formunda girdiyi koruma ve yeniden yükleme; fiyat değişince yeni tutarı onaylama; duyuru düzenleme; oturum kaybı; mobil katalog/sepet; klavyeyle modal ve Escape sonrası odak dönüşü; katalog ağ hatası.
  - Doğrulama: güncel Chromium CI **10/10**. Oturum kaybı cookie temizlenerek simüle edilir; gerçek zaman dolması beklenmez. Başka tarayıcı motorları bu kapsamda değildir.

- [x] **R07 · Dosya sorumluluklarını ve kaynak kurallarını düzenle.**
  - Kanıt: `95dab5c`, `d1c18bf`, `d4ddb3e`.
  - Entity'ler `Domain/Entities`, istek modelleri `Domain/Contracts`, doğrulama `Domain/Validation` altında. Program.cs okunur bloklara ayrıldı. Mevcut admin özellik ayrımı korundu; EF eşlemeleri BusinessDbContext içinde kalıyor.
  - `.editorconfig` ve `tests/verify-source.ps1`: temel boşluk/satır sonu kuralları, import sürümü tutarlılığı ve tek ortak state tanımı kontrol ediliyor.
  - Doğrulama: kaynak denetimi, derlemeler ve testler CI'da başarılı. Bu denetim tam bir formatter/linter değildir; bin/obj gibi üretilen dizinleri dışlama ileride küçük bir bakım iyileştirmesi olabilir.

- [x] **R08 · Grid ve duyuru eski form çakışmalarını ele al.**
  - Kanıt: `3c1cd11`, `0566207`; `005-admin-rowversion.sql` ve yönetim endpoint/formları.
  - Rowversion ve `GRID_CHANGED` / `BANNER_CHANGED` yanıtları eklenmiş. Form girdileri hata anında korunuyor; güncel veriyi yükleme eylemi var. Grid güncellemesi transaction içinde.
  - Doğrulama: iki admin istemcisiyle eski formun diğer değişikliği ezmesini engelleyen API testi başarılı. Grid/duyuru conflict arayüzünün bütün dalları ayrı browser senaryosu olarak henüz ölçülmüyor; kaynak kodu incelendi.

- [x] **R09 · Yüklenen ürün görsellerini çözümleyerek doğrula.**
  - Kanıt: `c75e822`, `1fca992`; `Services/ProductImageValidator.cs`.
  - SkiaSharp ile PNG/JPEG/WebP gerçekten çözümleniyor; bozuk/eksik görüntüler reddediliyor. En büyük kenar 4096 piksel, toplam 12 megapiksel sınırı ve mevcut 4 MB yükleme sınırı var.
  - Doğrulama: geçerli üç biçim ile kesik/aşırı boyutlu görsel testleri API suite'inde başarılı.
  - Yetim görseller otomatik silinmiyor. README, yedek alıp Products.ImageUrl referanslarıyla karşılaştırarak yalnız kullanılmayan dosyaları temizleme politikasını tanımlıyor. Otomatik temizleme aracı eklenmiş değildir; bu maddenin ölçütü güvenli politikanın tanımlanmasıydı.

## Üretim ve ölçek kapsamı

- **R14 · Kapsam dışı — üretim yayını.** Kullanıcı projenin canlıya çıkmayacağını belirtti. HTTPS dağıtımı, kalıcı anahtar/yükleme alanı, üretim yöneticisi ve yedekten dönüş tatbikatı yapılmış sayılmaz; şu anda bekleyen zorunlu görev değildir. Yayın kararı değişirse yeniden açılır.
- **R15 · Ertelendi — büyük veri performans ölçümü.** Ölçüm kanıtı yoktur; mevcut demo için zorunlu değildir. Gerçek bir performans ihtiyacı oluşursa kapsam ve hedef ölçüler belirlenir.

## Sunum ve teslim

- [x] **D01 · README ekran görüntüleri.** 2 Ekim 2026: `assets/screenshots` altında 15 güncel demo ekran görüntüsü mevcut; mobil kart sepeti, yönetim özeti, Türkçe geçmiş ve görsel kurtarma dahil. Tümü görsel olarak incelendi; tarayıcı hata kaydı boş.
- [x] **D02 · Plan yönetimi.** Önceden yerel olan ROADMAP.md ve AGENTS.md, 2 Ekim kullanıcı talimatıyla Git takibine alındı. README'de bu dosyalara referans yok.
- [x] **Kullanıcı isteği · Demo kapsamının görünür açıklanması.** Durum: tamamlandı | 2026-10-04 | README başlığına ve girişine yalnız demo/portföy uyarısı eklendi; gerçek yayına geçiş sınırı açıklaştırıldı ve GitHub depo açıklaması "Demo-only" olarak güncellendi. Test: `git diff --check` başarılı; `tests/verify-source.ps1` başarılı (78 C#/JS dosyası); [Build CI](https://github.com/yutkuz/b2b-commerce/actions/runs/37219055279) derleme, API ve Chromium testleri başarılı. Uygulama commit'i: `d7c885c` (yalnız dokümantasyon). Kalan sınırlama: gerçek üretim dağıtımı ve harici entegrasyonlar bu demo kapsamı dışındadır.

## Başlangıç ve tamamlanma kuralı

Eski dokuz aşama ve R24–R27 tamamlandı. Aktif zorunlu görev yok. R22 ürün kararı, R14 üretim kararı, R15 ölçülmüş performans ihtiyacı bekler; kendiliğinden başlatılmaz. Yeni bulgular kanıtlarıyla ayrı madde olarak açılır.

Her görev için kanıt satırı: `Tamamlanma: YYYY-AA-GG | commit: SHA | test: sonuç | sınırlama: varsa`.
Yeni SQL değişiklikleri `Data/SCHEMA.md` stratejisine uyar. İşleme göre API, eşzamanlılık ve tarayıcı testleri eklenir; mevcut sipariş/stock/snapshot davranışı korunur. Tamamlanmış R01–R09 tekrar uygulanmaz; yeni bakım bulguları R24–R27 altında takip edilir.
