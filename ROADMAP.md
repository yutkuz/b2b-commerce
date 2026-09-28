# U1 Business geliştirme yol haritası

Son inceleme: **28 Eylül 2026** · İncelenen uygulama: **`17a09bf`**

Amaç: Çalışan bayi sipariş akışını koruyarak veri güvenilirliğini, bakım kolaylığını ve ürünün kullanılabilirliğini artırmak. İlk kod düzeltmesi **R01** maddesidir; eski yerel kopyada çalışılacaksa önce **R00** tamamlanır. Bu dosyanın oluşturulması, listedeki uygulama değişikliklerinin yapıldığı anlamına gelmez.

## Planın yönetimi

- Plan sahibi **Astra / ana yönetici ajan**dır. Madde ekleme, kaldırma, kapsam ve öncelik değiştirme, yeniden sıralama ve kabul ölçütlerini değiştirme ona aittir. Kullanıcının talimatları önceliklidir.
- Uygulayıcı ajan, kendisine verilen maddenin kabul ölçütleri sağlandığında yalnız ilgili kutuyu `[x]` yapabilir ve altına tamamlanma kanıtını ekleyebilir. Kısmi iş tamamlandı sayılmaz; test çalıştırılamadıysa kutu açık kalır.
- Kanıt biçimi: `Tamamlanma: YYYY-AA-GG | uygulama commit'i: <SHA/link> | doğrulama: <test ve sonuç> | kalan sınırlama: <varsa>`.
- Uygulama commit'inden sonra kanıt ayrı belge commit'iyle eklenebilir; böylece dosyada kendi commit'inin SHA'sını tahmin etmek gerekmez.
- Ana yönetici ajan değişikliği kontrol eder; doğrulanmayan maddeyi yeniden açabilir. Yeni bulgu uygulayıcı tarafından raporlanır, planın kapsamı kendiliğinden genişletilmez.
- Her madde ayrı, anlamlı değişiklik olarak yapılır. İlgili testler geçmeden push yapılmaz. Kullanıcının eşzamanlı değişiklikleri korunur; güncel uzak dal kontrol edilir.
- Bunlar çalışma kurallarıdır; GitHub'da teknik bir dosya erişim kısıtlaması oluşturmaz.

## Mevcut durum ve inceleme kanıtı

- [x] EF Core veri erişimi ve SQL-first şema sahipliği tanımlandı. Kanıt: `3070f0f`, `17a09bf`, [şema stratejisi](src/U1.Business/Data/SCHEMA.md).
- [x] Yönetim arayüzü ve endpoint'ler özelliklere göre ayrıldı. Kanıt: `ae30cfa`.
- [x] Checkout deadlock denemeleri tükenince `503 / CHECKOUT_RETRY` yanıtı eklendi. Kanıt: `16a3053`; son sürümde kod mevcut. Zorlanmış hata testi R02'de açık kalıyor.
- [x] API testleri benzersiz veritabanına taşındı, API ve tarayıcı testleri çözüme alındı. Kanıt: `1460948`, `5e37287`, `c7c2d5b`.
- [x] Chromium bayi checkout akışı CI'a eklendi. Kanıt: `22346b5`, `cdc858e`.
- [x] Son uygulama sürümünün CI'ı geçti. [17a09bf için Build](https://github.com/yutkuz/b2b-commerce/actions/runs/36447529334): uygulama derlemesi, JavaScript sözdizimi, API ve Chromium testleri başarılı.
- [x] 28 Eylül yerel kontrolü: Release çözüm derlemesi **0 hata, 32 xUnit1051 uyarısı**; **7/7 API testi** başarılı. Demo arayüzünde bayi girişi, sepete ekleme, sepet ve yönetici ekranları incelendi.

Tarayıcı otomasyonunda şu an **bir temel bayi senaryosu** var. Yeşil CI, aşağıdaki kilit başarısızlığı, yükseltme ve bütün yönetici ekranlarını doğrulamıyor. Bu inceleme tam bir sızma testi veya yük testi değildir.

## 1. Veri güvenilirliği — önce yapılacaklar

- [ ] **R00 · Çalışma önkoşulu · Eski yerel çalışma kopyasını güvenle güncelle.**
  - İnceleme sırasında masaüstündeki ana checkout `5eb20ba` sürümünde, incelenen remote'un 25 commit gerisindeydi. `OrderService.cs` ve eski `tests/api_smoke.py` için kaydedilmemiş değişiklikler ile izlenmeyen `main` dosyası vardı. Bu inceleme güncel, ayrı checkout'ta yapıldı; eski dosyalar korunuyor.
  - **Kabul:** mevcut yerel değişiklikler geri alınabilir biçimde korunup güncel uygulamayla karşılaştırılır; gerekli parçalar kontrollü taşınır. Güncel dal ve temiz/hesabı verilmiş çalışma ağacı doğrulanır. Eski Python paketi veya eski sipariş servisi yanlışlıkla geri getirilmez; kullanıcıya hangi checkout'ta devam edileceği bildirilir.

- [ ] **R01 · P1 · SQL uygulama kilitlerinin gerçek dönüş kodunu kontrol et.**
  - **Doğrulanmış hata:** `EXEC sp_getapplock ...` sonrasında `ExecuteScalarAsync()` sonuç kümesi bekliyor; prosedürün dönüş kodu alınmıyor. `null`, `Convert.ToInt32` ile `0` oluyor. Kilit alınamadığında kontrol başarı sanabiliyor.
  - Konumlar: [OrderService.LockRequest](src/U1.Business/Services/OrderService.cs), [Database.Initialize ve ExecuteScalarAsync](src/U1.Business/Data/Database.cs). Sipariş anahtarı, veritabanı oluşturma ve şema yükseltme kilitlerinin tamamını düzelt.
  - Kanıt: ayrı demo DB'de iki SQL bağlantısı aynı kaynağı istedi. İkinci bağlantıda mevcut sorgu `null → 0`, açıkça alınan dönüş kodu **`-1`** verdi. Bu, hata algılama kusurunu doğrular; veri kaybı veya çift sipariş gerçekleştiği iddia edilmiyor.
  - Uygulama: dönüş değerini `EXEC @result = ...; SELECT @result` veya ADO.NET return-value parametresiyle al. Kilit zaman aşımı ile komut zaman aşımını bilinçli ayarla; başarısızlıkta işlem devam etmesin.
  - **Kabul:** gerçek SQL üzerinde kilit alınması, bekleme/zaman aşımı ve başarısızlıkta hiçbir sipariş/stok/şema değişikliği olmaması test edilir. Hata kontrollü yanıt üretir; transaction geri alınır. [SQL Server dönüş kodları](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql).

- [ ] **R02 · P1 · Eşzamanlılık ve hata sonrası yeniden deneme kapsamını tamamla.**
  - R01 sonrasında yap. Şu an 8 bayi testi yeterli stokla tek ürün kullanıyor; üçüncü deadlock'un gerçekten `503` verdiği test edilmiyor.
  - Aynı bayinin aynı istek anahtarını eşzamanlı göndermesi, farklı bayilerin aynı anahtarı kullanması, son stok için yarış, çok ürünlü sepet, sipariş reddiyle eşzamanlı checkout senaryolarını ekle.
  - `ChangeStatus` için deadlock/timeout davranışını ölç; checkout dışındaki genel 500'leri kontrollü ve güvenli tekrar davranışına dönüştür. Yeniden deneme yalnız geri alınmış transaction için yapılmalı.
  - **Kabul:** stok negatif olmaz; aynı sipariş bir kez oluşur; red stokları bir kez iade eder; rollback sonrası kısmi kayıt kalmaz. Yeniden deneme sınırı ve istemcinin aynı anahtarı koruması deterministik testle doğrulanır.

- [ ] **R03 · P1 · Eski veritabanını yükseltme ve EF/SQL uyumu için test ekle.**
  - Mevcut suite temiz DB oluşturuyor. `Data/SCHEMA.md` stratejisi açık; mevcut kayıtları koruyan yükseltme yolu için otomatik kanıt eksik.
  - **Kabul:** eski şemadan güncele geçişte kullanıcı, parola hash'i, sipariş kalemleri ve stok korunur; ikinci açılış aynı yükseltmeyi tekrarlamaz. Daha yeni şema reddedilir. Temel tür/uzunluk, decimal hassasiyeti, varsayılan değer, rowversion ve ilişki eşleşmeleri gerçek SQL üzerinde denetlenir. SQL-first yaklaşımı korunur.

## 2. Testler ve CI

- [ ] **R04 · P2 · Test veritabanlarının yaşam döngüsünü tamamla.**
  - **Doğrulanmış eksik:** `ApiFactory` benzersiz DB oluşturuyor ama kaldırmıyor; `BrowserTestApplication.DisposeAsync` yalnız uygulama sürecini kapatıyor. Yerel çalıştırmalar veritabanı biriktiriyor.
  - **Kabul:** başarıda ve hata durumunda yalnız o çalıştırmanın oluşturduğu tam DB adı temizlenir; bağlantılar kapanır. Ad/aidiyet kontrolü olmadan genel prefix'e göre toplu silme yapılmaz. İnceleme için DB'yi koruma seçeneği belgelenir; paralel iki süreç birbirini etkilemez.

- [ ] **R05 · P2 · CI çıktısını teşhis edilebilir ve uyarıları yönetilebilir hale getir.**
  - 32 adet `xUnit1051` uyarısında test iptal belirteçlerini uygun çağrılara ilet; uyarıları topluca susturma. Ardından test projelerinde de uyarı politikasını sıkılaştır.
  - **Kabul:** CI test raporlarını, başarısız tarayıcı adımının ekran görüntüsünü/trace'ini ve ilgili sunucu günlüğünü artifact olarak saklar; sırlar kayda girmez. İptal edilen test kendi uygulama sürecini temizler. README komutları CI ile uyumludur.

- [ ] **R06 · P2 · Tarayıcı davranış testlerini kritik yönetim ve hata akışlarına genişlet.**
  - Yönetici onay/red → bayi durum görüntüleme; eski ürün formu çakışması; fiyat değişince yeniden onay; oturum süresi dolması; ağ hatasından sonra sipariş sorgulama.
  - **Kabul:** yalnız URL veya satırın varlığı değil, doğru sipariş, tutar, durum ve kullanıcı mesajı doğrulanır. Mobil genişlikte katalog/sepet ve klavyeyle modal açma-kapama/odak dönüşü denenir. Sabit beklemeler kullanılmaz.

## 3. Kod ve kullanıcı deneyimi

- [ ] **R07 · P2 · Kalan dosya sorumluluklarını ayır, ortak biçim kuralları ekle.**
  - Mevcut `Endpoints/Admin` ve `wwwroot/admin` ayrımı uygun. Baştan mimari değişimi gerekmiyor.
  - `BusinessDbContext.cs` içindeki entity sınıflarını `Domain/Entities` altına; EF eşlemelerini gerektiğinde `Data/Configurations` altına taşı. `Domain/Models.cs` içindeki istek modellerini `Contracts` altında alanlarına göre ayır. `Program.cs` güvenlik ve hata katmanındaki sıkıştırılmış satırları aç.
  - `.editorconfig` ve seçilen C#/JS biçim denetimini ekle; bağımlılık eklemek için sırf klasör düzenini gerekçe yapma.
  - **Kabul:** davranış, JSON sözleşmesi, SQL şeması ve route'lar değişmez; mevcut testler geçer. Modül import'ları tek ortak state örneğini kullanır; elle yazılan sürüm eklerinin tutarlılığı kontrol edilir.

- [ ] **R08 · P2 · Grid ve duyuru düzenlemesinde eski form çakışmasını ele al.**
  - Ürün ve kullanıcıda sürüm kontrolü var; grid ve duyuru güncellemesinde yok. İki yönetici aynı alanı değiştirirse son kayıt önceki değişikliği sessizce ezebilir.
  - **Kabul:** eski form kaydı anlaşılır çakışma mesajı verir; form girdileri korunur, güncel veriyi yükleme yolu sunulur. İki istemcili API testi eklenir.

- [ ] **R09 · P2 · Ürün görseli yükleme doğrulamasını güçlendir.**
  - Mevcut kontrol dosya boyutu ve başlangıç imzasına dayanıyor; görüntünün gerçekten çözümlenebilirliğini ve piksel sınırını doğrulamıyor.
  - **Kabul:** bozuk/truncated dosya ve aşırı boyutlu görüntü kontrollü reddedilir; geçerli PNG/JPEG/WebP çalışır. Yetim görseller için kullanım ilişkisi ve güvenli temizlik politikası tanımlanır; kullanılan görseller silinmez.

## 4. Ürün geliştirmeleri — güvenilirlik işleri sonrasında

Bu bölüm yeni özellik önerileridir; mevcut ödevin hatalı olduğu anlamına gelmez. Uygulamaya alınacak kapsamı plan sahibi kullanıcıyla netleştirir.

- [ ] **R10 · P3 · Kategori yönetimi ve ürün arşivleme.** Kategoriyi ekleme/düzenleme; kullanılan kategoriyi güvenli taşıma; ürünü fiziksel silmeden satışa kapatma. **Kabul:** eski sipariş snapshot'ları korunur; arşivlenen ürün yeni siparişe giremez; mevcut sepette açıklayıcı mesaj gösterilir.
- [ ] **R11 · P3 · Stok hareketleri ve yönetici işlem geçmişi.** Stok düzeltmesi, sipariş düşümü ve red iadesi için kim/ne zaman/neden kaydı. **Kabul:** hareket ve iş işlemi aynı transaction ile kaydolur; parola ve hassas veri loglanmaz; yetkili kullanıcı filtreleyerek geçmişi görür.
- [ ] **R12 · P3 · Bayi profil yönetimi ve hesap kurtarma.** Bayinin kendi profilini düzenlemesi; doğrulanmış kurtarma kanalı seçildikten sonra süreli, tek kullanımlık parola yenileme. **Kabul:** başka hesaba erişilemez; eski oturumlar gereken durumlarda iptal edilir; hesap varlığını ifşa etmeyen yanıtlar ve hız sınırı test edilir.
- [ ] **R13 · P3 · Sipariş operasyonunu genişlet.** Sevk/teslim durumları ve sipariş çıktısı; geçiş kuralları ile stok etkisini önceden tanımla. **Kabul:** geçersiz geçişler reddedilir; tekrar istekleri ikinci stok hareketi yaratmaz; durum geçmişi görünür.

## 5. Gerçek kullanıma geçmeden önce

- [ ] **R14 · Yayın önkoşulu · Üretim kurulumunu ve geri dönüşü doğrula.** HTTPS, kalıcı Data Protection anahtarları/yüklemeler, güçlü ilk yönetici kurulumu, uygulama hesabının SQL yetkileri, yedek ve geri yükleme; demo katalog ve gerçek katalog ayrımı. **Kabul:** ayrı bir test ortamında kurulum, yeniden başlatma ve yedekten dönüş uygulanır; gerçek veriler korunur. LocalDB geliştirme varsayımı üretim kurulumu olarak sunulmaz.
- [ ] **R15 · Ölçek ihtiyacında · Katalog ve sipariş performansını ölç.** Temsili büyük katalogda arama/sayfalama ve çok ürünlü checkout SQL sorgu sayısını, gecikmeleri ve kilit beklemelerini ölç. **Kabul:** veri boyutu ve donanımla birlikte başlangıç ölçümü kaydedilir; indeks/sorgu değişikliği ölçülen darboğaza dayanır. Sırf modern görünmek için cache/mikroservis eklenmez.

## Sunum ve teslim

- [x] **D01 · README'ye gerçek arayüz ekran görüntüleri ekle.** 28 Eylül 2026, `17a09bf` uygulaması, ayrı demo DB: katalog, sepet ve yönetim paneli. Kaynak: [assets/screenshots](assets/screenshots). Görseller yalnız örnek veriler içerir; gerçek kullanıcı verileri kullanılmadı.
- [x] **D02 · Yol haritası yönetim kuralını tanımla.** Bu dosya ve kökteki [AGENTS.md](AGENTS.md). Tamamlanma kutuları, kanıt biçimi ve plan sahibinin yetkisi belirlendi.

**Önerilen sıra:** eski yerel checkout kullanılacaksa R00; ardından R01 → R02 → R03 → R04 → R05 → R06 → R07 → R08 → R09. R10–R13 ürün önceliğine göre seçilir. R14 gerçek yayına çıkılacaksa öncelik kazanır; R15 ölçülen ihtiyaca göre başlatılır.
