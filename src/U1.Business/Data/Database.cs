using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using System.Data;
using U1.Business.Domain;

namespace U1.Business.Data;

public sealed class Database(IConfiguration config)
{
    public SqlConnection Open() => new(config.GetConnectionString("SqlServer"));
    public async Task Initialize(bool development)
    {
        var builder = new SqlConnectionStringBuilder(config.GetConnectionString("SqlServer"));
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Veritabanı adı yalnızca harf, rakam ve alt çizgi içerebilir.");
        var expectedTestDatabase = Environment.GetEnvironmentVariable("U1_TEST_DATABASE");
        if (expectedTestDatabase is not null && (name != expectedTestDatabase || name == "U1Business"))
            throw new InvalidOperationException("Test veritabanı bağlantısı beklenen ayrı veritabanıyla eşleşmiyor.");
        builder.InitialCatalog = "master";
        using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            var resource = "U1Business.Create." + name;
            var createLock = await master.ExecuteScalarAsync<int>("EXEC sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=30000",new { resource });
            if(createLock<0) throw new InvalidOperationException("Veritabanı oluşturma kilidi alınamadı.");
            try { await master.ExecuteAsync($"IF DB_ID(@name) IS NULL CREATE DATABASE [{name}]",new { name }); }
            finally { await master.ExecuteAsync("EXEC sp_releaseapplock @Resource=@resource, @LockOwner='Session'",new { resource }); }
        }
        using var db = Open();
        var assembly = typeof(Database).Assembly;
        await db.OpenAsync();
        using (var tx = db.BeginTransaction(IsolationLevel.Serializable))
        {
            var lockResult = await db.ExecuteScalarAsync<int>("EXEC sp_getapplock @Resource='U1Business.Schema', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=30000", transaction: tx);
            if (lockResult < 0) throw new InvalidOperationException("Şema yükseltme kilidi alınamadı.");
            using var stream = assembly.GetManifestResourceStream("U1.Business.Data.001-schema.sql")!;
            using var reader = new StreamReader(stream);
            await db.ExecuteAsync(await reader.ReadToEndAsync(), transaction: tx);
            var version = await db.ExecuteScalarAsync<int>("SELECT MAX(Version) FROM dbo.SchemaVersions", transaction: tx);
            if (version > 3) throw new InvalidOperationException("Veritabanı şeması bu uygulamadan daha yeni.");
            if (version < 2)
            {
                using var migration = assembly.GetManifestResourceStream("U1.Business.Data.003-product-rowversion.sql")!;
                using var migrationReader = new StreamReader(migration);
                await db.ExecuteAsync(await migrationReader.ReadToEndAsync(), transaction: tx);
                await db.ExecuteAsync("INSERT INTO dbo.SchemaVersions(Version) VALUES(2)", transaction: tx);
            }
            if (version < 3)
            {
                using var setupMigration = assembly.GetManifestResourceStream("U1.Business.Data.004-demo-setup.sql")!;
                using var setupReader = new StreamReader(setupMigration);
                await db.ExecuteAsync(await setupReader.ReadToEndAsync(), transaction: tx);
                await db.ExecuteAsync("INSERT INTO dbo.SchemaVersions(Version) VALUES(3)", transaction: tx);
            }
            await InitializeDemo(db, tx, development);
            using var visualStream = assembly.GetManifestResourceStream("U1.Business.Data.002-demo-visuals.sql")!;
            using var visualReader = new StreamReader(visualStream);
            await db.ExecuteAsync(await visualReader.ReadToEndAsync(), transaction: tx);
            tx.Commit();
        }
    }
    private static async Task InitializeDemo(SqlConnection db, SqlTransaction tx, bool development)
    {
        var catalogDone = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DemoSetup WHERE Component='catalog'", transaction: tx) != 0;
        if (!catalogDone)
        {
            var categoryCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Categories", transaction: tx);
            var productCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products", transaction: tx);
            var columnCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM GridColumns", transaction: tx);
            var bannerCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Banners", transaction: tx);
            var empty = categoryCount == 0 && productCount == 0 && columnCount == 0 && bannerCount == 0;
            if (empty) await Seed(db, tx);
            else
            {
                var demoCategories = new[] { "Diagnostik cihazlar", "Elektronik parçalar", "Servis ekipmanları", "Bağlantı & kablolar" };
                var demoCodes = new[] { "DG-001", "DG-002", "EL-101", "EL-102", "SR-201", "KB-301", "SR-202", "EL-103", "KB-302", "SR-203", "EL-104", "DG-003" };
                var onlyDemoCategories = categoryCount <= demoCategories.Length &&
                    await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Categories WHERE Name NOT IN @names", new { names=demoCategories }, tx) == 0;
                var onlyDemoProducts = productCount <= demoCodes.Length &&
                    await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Code NOT IN @codes", new { codes=demoCodes }, tx) == 0;
                if (onlyDemoCategories && onlyDemoProducts && (categoryCount < 4 || productCount < 12 || columnCount == 0))
                    throw new InvalidOperationException("Demo katalog kurulumu yarım kalmış olabilir. Mevcut verileri koruyarak kurtarma için README'deki başlangıç verisi bölümünü izleyin.");
            }
            await db.ExecuteAsync("INSERT INTO DemoSetup(Component,Status) VALUES('catalog',@status)", new {status=empty?"seeded":"existing"}, tx);
        }
        if (!development) return;
        var accountsDone = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM DemoSetup WHERE Component='accounts'", transaction: tx) != 0;
        if (accountsDone) return;
        var userCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users", transaction: tx);
        if (userCount == 0)
        {
            var hash = new PasswordHasher<User>();
            foreach (var pair in new[] { ("admin@u1.local", "Admin", "Yönetici", "U1 Business", "U1Admin!2026"), ("bayi@u1.local", "Dealer", "Ahmet", "Yılmaz Otomotiv", "U1Bayi!2026") })
            {
                var user = new User { Email = pair.Item1 };
                await db.ExecuteAsync("INSERT INTO Users(FirstName,LastName,Email,Phone,Company,PasswordHash,Role) VALUES(@first,N'Yılmaz',@email,'05321234567',@company,@hash,@role); INSERT INTO Carts(UserId) VALUES(SCOPE_IDENTITY());",
                    new { first = pair.Item3, email = pair.Item1, company = pair.Item4, hash = hash.HashPassword(user, pair.Item5), role = pair.Item2 }, tx);
            }
        }
        else
        {
            var demoCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Users WHERE Email IN ('admin@u1.local','bayi@u1.local')", transaction: tx);
            if (demoCount == 1)
                throw new InvalidOperationException("Demo kullanıcı kurulumu yarım kalmış olabilir. Mevcut kullanıcıyı koruyarak kurtarma için README'deki başlangıç verisi bölümünü izleyin.");
            await db.ExecuteAsync("INSERT INTO Carts(UserId) SELECT u.Id FROM Users u WHERE NOT EXISTS(SELECT 1 FROM Carts c WHERE c.UserId=u.Id)", transaction: tx);
        }
        await db.ExecuteAsync("INSERT INTO DemoSetup(Component,Status) VALUES('accounts',@status)", new {status=userCount==0?"seeded":"existing"}, tx);
    }
    private static async Task Seed(SqlConnection db, SqlTransaction tx)
    {
        await db.ExecuteAsync("INSERT INTO Categories(Name) VALUES(N'Diagnostik cihazlar'),(N'Elektronik parçalar'),(N'Servis ekipmanları'),(N'Bağlantı & kablolar');", transaction: tx);
        var categoryIds = (await db.QueryAsync<int>("SELECT Id FROM Categories ORDER BY Id", transaction: tx)).ToArray();
        var items = new[] {
            new {code="DG-001",name="Profesyonel arıza tespit cihazı",brand="U1 Diagnostic",maker="PRO-X7",cat=1,price=24900m,stock=24,critical=5,desc="OBD-II uyumlu araçlarda kapsamlı arıza okuma ve canlı veri takibi. 10 inç dokunmatik ekran, kablosuz bağlantı.",img="diagnostic"},
            new {code="DG-002",name="Kompakt OBD-II test cihazı",brand="U1 Diagnostic",maker="SCAN-S2",cat=1,price=3850m,stock=8,critical=10,desc="Servis ve saha kullanımı için taşınabilir hata kodu okuyucu. OBD-II protokolleriyle uyumlu.",img="diagnostic"},
            new {code="EL-101",name="Motor kontrol ünitesi",brand="U1 Electronics",maker="ECU-240",cat=2,price=12750m,stock=16,critical=4,desc="Otomotiv motor yönetimi için elektronik kontrol modülü. Sipariş öncesinde araç uyumluluğunu kontrol edin.",img="module"},
            new {code="EL-102",name="ABS sensörü · ön teker",brand="U1 Electronics",maker="ABS-F01",cat=2,price=680m,stock=64,critical=15,desc="Ön teker hız ölçümü için hassas ABS sensörü. Kablo ve bağlantı soketi dahildir.",img="cable"},
            new {code="SR-201",name="Dijital akü test cihazı",brand="U1 Service",maker="BAT-600",cat=3,price=2450m,stock=3,critical=5,desc="12V aküler için şarj, marş ve sağlık kontrolü. Kolay okunur dijital ekran.",img="diagnostic"},
            new {code="KB-301",name="OBD-II bağlantı kablosu",brand="U1 Connect",maker="OBD-16P",cat=4,price=450m,stock=120,critical=20,desc="16 pin OBD-II uzatma kablosu, 1,5 metre. Yoğun servis kullanımına uygun dayanıklı bağlantılar.",img="cable"},
            new {code="SR-202",name="Profesyonel dijital multimetre",brand="U1 Service",maker="DMM-820",cat=3,price=1890m,stock=0,critical=5,desc="Gerilim, akım ve direnç ölçümü için otomatik aralıklı dijital multimetre.",img="diagnostic"},
            new {code="EL-103",name="Oksijen sensörü",brand="U1 Electronics",maker="O2-4W",cat=2,price=1250m,stock=32,critical=8,desc="Dört kablolu oksijen sensörü. Yakıt kontrol sistemleri için yedek parça.",img="cable"},
            new {code="KB-302",name="Diagnostik adaptör seti",brand="U1 Connect",maker="ADP-8",cat=4,price=3200m,stock=19,critical=5,desc="Farklı araç bağlantıları için 8 parçalı diagnostik adaptör seti.",img="cable"},
            new {code="SR-203",name="Elektronik test probu seti",brand="U1 Service",maker="PROBE-12",cat=3,price=950m,stock=47,critical=10,desc="Hassas elektronik ölçümler için 12 parçalı test probu ve bağlantı aksesuarları.",img="cable"},
            new {code="EL-104",name="Röle kontrol modülü",brand="U1 Electronics",maker="RLY-12V",cat=2,price=790m,stock=28,critical=6,desc="12V otomotiv devreleri için kompakt röle kontrol ünitesi.",img="module"},
            new {code="DG-003",name="Kablosuz diagnostik arayüz",brand="U1 Diagnostic",maker="VCI-BT",cat=1,price=6900m,stock=11,critical=5,desc="Uyumlu diagnostik yazılımlarla Bluetooth üzerinden araç veri bağlantısı.",img="module"}
        };
        foreach (var p in items)
            await db.ExecuteAsync("INSERT INTO Products(Code,Name,Description,Brand,ManufacturerCode,SpecialCode1,SpecialCode2,ImageUrl,Stock,CriticalStock,Price,CategoryId) VALUES(@code,@name,@desc,@brand,@maker,@special1,'2026',@image,@stock,@critical,@price,@cat)",
                new { p.code,p.name,p.desc,p.brand,p.maker,p.stock,p.critical,p.price,cat=categoryIds[p.cat-1], special1="AUTO-"+p.code[..2],image="/images/"+p.img+".svg" }, tx);
        foreach (var c in new[] {
            new GridColumn {Field="imageUrl",Label="Ürün",RenderType="image",Position=0,Width=76,Mobile=false},
            new GridColumn {Field="code",Label="Ürün kodu",Position=1,Width=112,Mobile=false},
            new GridColumn {Field="name",Label="Ürün adı",RenderType="product",Position=2,Width=290},
            new GridColumn {Field="brand",Label="Marka",Position=3,Width=150,Mobile=false},
            new GridColumn {Field="stock",Label="Stok",RenderType="stock",Position=4,Width=105,Mobile=false},
            new GridColumn {Field="price",Label="Birim fiyat",RenderType="money",Position=5,Width=125,Align="right"},
            new GridColumn {Field="quantity",Label="Adet / Sepete ekle",RenderType="purchase",Position=6,Width=180,Align="right"},
            new GridColumn {Field="manufacturerCode",Label="Üretici kodu",Position=7,Width=130,Desktop=false,Tablet=false,Mobile=false},
            new GridColumn {Field="specialCode1",Label="Özel kod 1",Position=8,Width=130,Desktop=false,Tablet=false,Mobile=false},
            new GridColumn {Field="specialCode2",Label="Özel kod 2",Position=9,Width=130,Desktop=false,Tablet=false,Mobile=false},
            new GridColumn {Field="description",Label="Açıklama",Position=10,Width=280,Desktop=false,Tablet=false,Mobile=false}})
            await db.ExecuteAsync("INSERT INTO GridColumns(Field,Label,RenderType,Position,Width,Align,Desktop,Tablet,Mobile) VALUES(@Field,@Label,@RenderType,@Position,@Width,@Align,@Desktop,@Tablet,@Mobile)", c, tx);
        await db.ExecuteAsync("INSERT INTO Banners(Title,Subtitle,ButtonText,SearchTerm,IsActive,Position) VALUES(N'Doğru teşhis. Güçlü servis.',N'Profesyonel diagnostik çözümlerini tek bir yerden keşfedin.',N'Cihazları incele',N'Diagnostik',1,0),(N'Her bağlantıda güven.',N'Servisinizin ihtiyaç duyduğu kablo ve adaptörler.',N'Bağlantı ürünleri',N'kablo',1,1);", transaction: tx);
    }
}
