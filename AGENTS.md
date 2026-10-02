# Proje çalışma kuralları

- Çalışmadan önce kökteki `ROADMAP.md` dosyasını oku ve görevin hangi maddeyle ilişkili olduğunu belirt.
- Yol haritasının kapsamını, sırasını, önceliklerini ve kabul ölçütlerini Astra / ana yönetici ajan yönetir. Kullanıcının açık talimatları önceliklidir.
- Uygulayıcı ajan yalnız kendisine verilen tamamlanmış maddenin kutusunu işaretleyebilir ve tarih, uygulama commit'i, doğrulama sonucu ile kalan sınırlamayı ekleyebilir. Kısmi veya doğrulanmamış işi tamamlandı sayma; yeni bulguları ana yöneticiye raporla.
- Kullanıcının mevcut değişikliklerini koru. Push öncesi uzak dalın güncel durumunu ve ilgili kontrolleri doğrula. Normal teknik commit açıklamaları kullan.
- `docs/`, yerel ayarlar, parolalar, veritabanları ve geçici test çıktıları depoya eklenmez. README ekran görüntüleri `assets/screenshots/` altında, yalnız demo verileriyle tutulur.
- Şema değişikliklerinde `src/U1.Business/Data/SCHEMA.md` içindeki SQL-first stratejisini izle.

- Kullanıcının 2 Ekim 2026 talimatıyla ROADMAP.md ve AGENTS.md GitHub'da sürümlenir. docs/, yerel ayarlar, veritabanları ve geçici çıktılar yerelde kalır.
- Her görev veya çalışma oturumu sonunda uygulayıcı ajan ROADMAP.md içindeki kendi maddesine durum (devam ediyor / doğrulama bekliyor / tamamlandı / engelli), tarih, değişiklik özeti, çalıştırılan testlerin gerçek sonucu, uygulama commit'i ve varsa kalan adımı yazar. Yalnız kabul ölçütleri doğrulanan kutuları işaretler. Commit henüz yoksa bunu açıkça belirtir; commit/push sonrasında SHA ve CI bağlantısı eklenir.
- Eşzamanlı ajanlar ROADMAP.md'de yalnız kendi görev bölümünü düzenler. Ana yönetici genel sıralamayı ve teslim özetini günceller. README kullanıcıya yönelik ürün ve kurulum belgesi olarak kalır.
