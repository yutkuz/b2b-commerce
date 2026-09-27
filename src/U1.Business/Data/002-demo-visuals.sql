-- Only replace original demo artwork. Preserve uploaded/custom images and commerce data.
UPDATE p SET ImageUrl = '/images/products/' + v.Photo
FROM dbo.Products p
JOIN (VALUES
 (N'DG-001', N'PRO-X7', 'tablet.png'),
 (N'DG-002', N'SCAN-S2', 'scanner.jpg'),
 (N'EL-101', N'ECU-240', 'ecu.jpg'),
 (N'EL-102', N'ABS-F01', 'abs.jpg'),
 (N'SR-201', N'BAT-600', 'battery.jpg'),
 (N'KB-301', N'OBD-16P', 'obd-cable.jpg'),
 (N'SR-202', N'DMM-820', 'multimeter.jpg'),
 (N'EL-103', N'O2-4W', 'oxygen.jpg'),
 (N'KB-302', N'ADP-8', 'adapter.jpg'),
 (N'SR-203', N'PROBE-12', 'probes.jpg'),
 (N'EL-104', N'RLY-12V', 'relay-auto.jpg'),
 (N'DG-003', N'VCI-BT', 'wireless.jpg')
) v(Code, ManufacturerCode, Photo) ON p.Code=v.Code AND p.ManufacturerCode=v.ManufacturerCode
WHERE p.ImageUrl IN ('/images/diagnostic.svg','/images/module.svg','/images/cable.svg');

UPDATE dbo.Banners SET Title=N'Diagnostik cihazlar kataloğu',
 Subtitle=N'Arıza tespit cihazları ve araç bağlantı ekipmanlarını ürün koduyla inceleyin.',
 ButtonText=N'Ürünleri incele'
WHERE Title=N'Doğru teşhis. Güçlü servis.' AND Subtitle=N'Profesyonel diagnostik çözümlerini tek bir yerden keşfedin.';

UPDATE dbo.Banners SET Title=N'Kablo ve bağlantı ürünleri',
 Subtitle=N'Sipariş öncesinde soket tipini ve araç uyumluluğunu kontrol edin.',
 ButtonText=N'Kataloğu aç'
WHERE Title=N'Her bağlantıda güven.' AND Subtitle=N'Servisinizin ihtiyaç duyduğu kablo ve adaptörler.';
