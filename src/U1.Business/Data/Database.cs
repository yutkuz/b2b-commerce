using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using U1.Business.Domain;

namespace U1.Business.Data;

public sealed class Database(
    IConfiguration config,
    IDbContextFactory<BusinessDbContext> dbFactory)
{
    private readonly int initializationLockTimeoutMilliseconds =
        config.GetValue<int?>("DatabaseLocks:InitializationTimeoutMilliseconds") ?? 30_000;

    public async Task Initialize(bool development)
    {
        var connectionString = config.GetConnectionString("SqlServer")
            ?? throw new InvalidOperationException("SqlServer bağlantı dizesi bulunamadı.");
        var builder = new SqlConnectionStringBuilder(connectionString);
        var name = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidOperationException("Veritabanı adı yalnızca harf, rakam ve alt çizgi içerebilir.");

        var expectedTestDatabase = Environment.GetEnvironmentVariable("U1_TEST_DATABASE");
        if (expectedTestDatabase is not null && (name != expectedTestDatabase || name == "U1Business"))
            throw new InvalidOperationException("Test veritabanı bağlantısı beklenen ayrı veritabanıyla eşleşmiyor.");

        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();
            var resource = "U1Business.Create." + name;

            var createLock = await SqlApplicationLock.AcquireAsync(
                master,
                transaction: null,
                resource,
                owner: "Session",
                initializationLockTimeoutMilliseconds);
            if (createLock < 0)
                throw new InvalidOperationException("Veritabanı oluşturma kilidi alınamadı.");

            try
            {
                using var create = master.CreateCommand();
                create.CommandText = $"IF DB_ID(@name) IS NULL CREATE DATABASE [{name}]";
                create.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = name });
                await create.ExecuteNonQueryAsync();
            }
            finally
            {
                await SqlApplicationLock.ReleaseAsync(master, resource, owner: "Session");
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Database.OpenConnectionAsync();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var assembly = typeof(Database).Assembly;

        var lockResult = await SqlApplicationLock.AcquireAsync(
            (SqlConnection)db.Database.GetDbConnection(),
            (SqlTransaction)db.Database.CurrentTransaction!.GetDbTransaction(),
            "U1Business.Schema",
            owner: "Transaction",
            initializationLockTimeoutMilliseconds);
        if (lockResult < 0)
            throw new InvalidOperationException("Şema yükseltme kilidi alınamadı.");

        await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.001-schema.sql"));
        var version = await db.SchemaVersions.MaxAsync(x => (int?)x.Version) ?? 0;
        if (version > 6)
            throw new InvalidOperationException("Veritabanı şeması bu uygulamadan daha yeni.");

        if (version < 2)
        {
            await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.003-product-rowversion.sql"));
            db.SchemaVersions.Add(new SchemaVersion { Version = 2 });
            await db.SaveChangesAsync();
        }

        if (version < 3)
        {
            await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.004-demo-setup.sql"));
            db.SchemaVersions.Add(new SchemaVersion { Version = 3 });
            await db.SaveChangesAsync();
        }

        if (version < 4)
        {
            await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.005-admin-rowversion.sql"));
            db.SchemaVersions.Add(new SchemaVersion { Version = 4 });
            await db.SaveChangesAsync();
        }

        if (version < 5)
        {
            await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.006-audit-history.sql"));
            db.SchemaVersions.Add(new SchemaVersion { Version = 5 });
            await db.SaveChangesAsync();
        }

        if (version < 6)
        {
            await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.007-catalog-management.sql"));
            db.SchemaVersions.Add(new SchemaVersion { Version = 6 });
            await db.SaveChangesAsync();
        }

        await InitializeDemo(db, development);
        await db.Database.ExecuteSqlRawAsync(await ReadResource(assembly, "U1.Business.Data.002-demo-visuals.sql"));
        await tx.CommitAsync();
    }

    private static async Task<string> ReadResource(System.Reflection.Assembly assembly, string name)
    {
        await using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Kaynak bulunamadı: {name}");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static async Task InitializeDemo(BusinessDbContext db, bool development)
    {
        var catalogDone = await db.DemoSetup.AsNoTracking().AnyAsync(x => x.Component == "catalog");
        if (!catalogDone)
        {
            var categoryCount = await db.Categories.CountAsync();
            var productCount = await db.Products.CountAsync();
            var columnCount = await db.GridColumns.CountAsync();
            var bannerCount = await db.Banners.CountAsync();
            var empty = categoryCount == 0 && productCount == 0 && columnCount == 0 && bannerCount == 0;

            if (empty)
            {
                await Seed(db);
            }
            else
            {
                var demoCategories = new[] { "Diagnostik cihazlar", "Elektronik parçalar", "Servis ekipmanları", "Bağlantı & kablolar" };
                var demoCodes = new[] { "DG-001", "DG-002", "EL-101", "EL-102", "SR-201", "KB-301", "SR-202", "EL-103", "KB-302", "SR-203", "EL-104", "DG-003" };
                var onlyDemoCategories = categoryCount <= demoCategories.Length
                    && !await db.Categories.AnyAsync(x => !demoCategories.Contains(x.Name));
                var onlyDemoProducts = productCount <= demoCodes.Length
                    && !await db.Products.AnyAsync(x => !demoCodes.Contains(x.Code));

                if (onlyDemoCategories && onlyDemoProducts && (categoryCount < 4 || productCount < 12 || columnCount == 0))
                    throw new InvalidOperationException("Demo katalog kurulumu yarım kalmış olabilir. Mevcut verileri koruyarak kurtarma için README'deki başlangıç verisi bölümünü izleyin.");
            }

            db.DemoSetup.Add(new DemoSetup
            {
                Component = "catalog",
                Status = empty ? "seeded" : "existing"
            });
            await db.SaveChangesAsync();
        }

        if (!development)
            return;

        var accountsDone = await db.DemoSetup.AsNoTracking().AnyAsync(x => x.Component == "accounts");
        if (accountsDone)
            return;

        var userCount = await db.Users.CountAsync();
        if (userCount == 0)
        {
            var hasher = new PasswordHasher<User>();
            var users = new[]
            {
                CreateDemoUser(hasher, "admin@u1.local", "Admin", "Yönetici", "U1 Business", "U1Admin!2026"),
                CreateDemoUser(hasher, "bayi@u1.local", "Dealer", "Ahmet", "Yılmaz Otomotiv", "U1Bayi!2026")
            };

            db.Users.AddRange(users);
            await db.SaveChangesAsync();
            db.Carts.AddRange(users.Select(x => new Cart { UserId = x.Id }));
            await db.SaveChangesAsync();
        }
        else
        {
            var demoCount = await db.Users.CountAsync(x => x.Email == "admin@u1.local" || x.Email == "bayi@u1.local");
            if (demoCount == 1)
                throw new InvalidOperationException("Demo kullanıcı kurulumu yarım kalmış olabilir. Mevcut kullanıcıyı koruyarak kurtarma için README'deki başlangıç verisi bölümünü izleyin.");

            var usersWithoutCart = await db.Users
                .Where(u => !db.Carts.Any(c => c.UserId == u.Id))
                .Select(u => u.Id)
                .ToListAsync();
            db.Carts.AddRange(usersWithoutCart.Select(id => new Cart { UserId = id }));
            await db.SaveChangesAsync();
        }

        db.DemoSetup.Add(new DemoSetup
        {
            Component = "accounts",
            Status = userCount == 0 ? "seeded" : "existing"
        });
        await db.SaveChangesAsync();
    }

    private static User CreateDemoUser(
        PasswordHasher<User> hasher,
        string email,
        string role,
        string firstName,
        string company,
        string password)
    {
        var user = new User
        {
            FirstName = firstName,
            LastName = "Yılmaz",
            Email = email,
            Phone = "05321234567",
            Company = company,
            Role = role,
            IsActive = true,
            AuthVersion = 1
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private static async Task Seed(BusinessDbContext db)
    {
        var categories = new[]
        {
            new Category { Name = "Diagnostik cihazlar" },
            new Category { Name = "Elektronik parçalar" },
            new Category { Name = "Servis ekipmanları" },
            new Category { Name = "Bağlantı & kablolar" }
        };
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync();

        var items = new[]
        {
            new Product { Code="DG-001", Name="Profesyonel arıza tespit cihazı", Brand="U1 Diagnostic", ManufacturerCode="PRO-X7", CategoryId=categories[0].Id, Price=24900m, Stock=24, CriticalStock=5, Description="OBD-II uyumlu araçlarda kapsamlı arıza okuma ve canlı veri takibi. 10 inç dokunmatik ekran, kablosuz bağlantı.", ImageUrl="/images/diagnostic.svg", SpecialCode1="AUTO-DG", SpecialCode2="2026" },
            new Product { Code="DG-002", Name="Kompakt OBD-II test cihazı", Brand="U1 Diagnostic", ManufacturerCode="SCAN-S2", CategoryId=categories[0].Id, Price=3850m, Stock=8, CriticalStock=10, Description="Servis ve saha kullanımı için taşınabilir hata kodu okuyucu. OBD-II protokolleriyle uyumlu.", ImageUrl="/images/diagnostic.svg", SpecialCode1="AUTO-DG", SpecialCode2="2026" },
            new Product { Code="EL-101", Name="Motor kontrol ünitesi", Brand="U1 Electronics", ManufacturerCode="ECU-240", CategoryId=categories[1].Id, Price=12750m, Stock=16, CriticalStock=4, Description="Otomotiv motor yönetimi için elektronik kontrol modülü. Sipariş öncesinde araç uyumluluğunu kontrol edin.", ImageUrl="/images/module.svg", SpecialCode1="AUTO-EL", SpecialCode2="2026" },
            new Product { Code="EL-102", Name="ABS sensörü · ön teker", Brand="U1 Electronics", ManufacturerCode="ABS-F01", CategoryId=categories[1].Id, Price=680m, Stock=64, CriticalStock=15, Description="Ön teker hız ölçümü için hassas ABS sensörü. Kablo ve bağlantı soketi dahildir.", ImageUrl="/images/cable.svg", SpecialCode1="AUTO-EL", SpecialCode2="2026" },
            new Product { Code="SR-201", Name="Dijital akü test cihazı", Brand="U1 Service", ManufacturerCode="BAT-600", CategoryId=categories[2].Id, Price=2450m, Stock=3, CriticalStock=5, Description="12V aküler için şarj, marş ve sağlık kontrolü. Kolay okunur dijital ekran.", ImageUrl="/images/diagnostic.svg", SpecialCode1="AUTO-SR", SpecialCode2="2026" },
            new Product { Code="KB-301", Name="OBD-II bağlantı kablosu", Brand="U1 Connect", ManufacturerCode="OBD-16P", CategoryId=categories[3].Id, Price=450m, Stock=120, CriticalStock=20, Description="16 pin OBD-II uzatma kablosu, 1,5 metre. Yoğun servis kullanımına uygun dayanıklı bağlantılar.", ImageUrl="/images/cable.svg", SpecialCode1="AUTO-KB", SpecialCode2="2026" },
            new Product { Code="SR-202", Name="Profesyonel dijital multimetre", Brand="U1 Service", ManufacturerCode="DMM-820", CategoryId=categories[2].Id, Price=1890m, Stock=0, CriticalStock=5, Description="Gerilim, akım ve direnç ölçümü için otomatik aralıklı dijital multimetre.", ImageUrl="/images/diagnostic.svg", SpecialCode1="AUTO-SR", SpecialCode2="2026" },
            new Product { Code="EL-103", Name="Oksijen sensörü", Brand="U1 Electronics", ManufacturerCode="O2-4W", CategoryId=categories[1].Id, Price=1250m, Stock=32, CriticalStock=8, Description="Dört kablolu oksijen sensörü. Yakıt kontrol sistemleri için yedek parça.", ImageUrl="/images/cable.svg", SpecialCode1="AUTO-EL", SpecialCode2="2026" },
            new Product { Code="KB-302", Name="Diagnostik adaptör seti", Brand="U1 Connect", ManufacturerCode="ADP-8", CategoryId=categories[3].Id, Price=3200m, Stock=19, CriticalStock=5, Description="Farklı araç bağlantıları için 8 parçalı diagnostik adaptör seti.", ImageUrl="/images/cable.svg", SpecialCode1="AUTO-KB", SpecialCode2="2026" },
            new Product { Code="SR-203", Name="Elektronik test probu seti", Brand="U1 Service", ManufacturerCode="PROBE-12", CategoryId=categories[2].Id, Price=950m, Stock=47, CriticalStock=10, Description="Hassas elektronik ölçümler için 12 parçalı test probu ve bağlantı aksesuarları.", ImageUrl="/images/cable.svg", SpecialCode1="AUTO-SR", SpecialCode2="2026" },
            new Product { Code="EL-104", Name="Röle kontrol modülü", Brand="U1 Electronics", ManufacturerCode="RLY-12V", CategoryId=categories[1].Id, Price=790m, Stock=28, CriticalStock=6, Description="12V otomotiv devreleri için kompakt röle kontrol ünitesi.", ImageUrl="/images/module.svg", SpecialCode1="AUTO-EL", SpecialCode2="2026" },
            new Product { Code="DG-003", Name="Kablosuz diagnostik arayüz", Brand="U1 Diagnostic", ManufacturerCode="VCI-BT", CategoryId=categories[0].Id, Price=6900m, Stock=11, CriticalStock=5, Description="Uyumlu diagnostik yazılımlarla Bluetooth üzerinden araç veri bağlantısı.", ImageUrl="/images/module.svg", SpecialCode1="AUTO-DG", SpecialCode2="2026" }
        };
        db.Products.AddRange(items);

        db.GridColumns.AddRange(
            new GridColumn { Field="imageUrl", Label="Ürün", RenderType="image", Position=0, Width=76, Mobile=false },
            new GridColumn { Field="code", Label="Ürün kodu", Position=1, Width=112, Mobile=false },
            new GridColumn { Field="name", Label="Ürün adı", RenderType="product", Position=2, Width=290 },
            new GridColumn { Field="brand", Label="Marka", Position=3, Width=150, Mobile=false },
            new GridColumn { Field="stock", Label="Stok", RenderType="stock", Position=4, Width=105, Mobile=false },
            new GridColumn { Field="price", Label="Birim fiyat", RenderType="money", Position=5, Width=125, Align="right" },
            new GridColumn { Field="quantity", Label="Adet / Sepete ekle", RenderType="purchase", Position=6, Width=180, Align="right" },
            new GridColumn { Field="manufacturerCode", Label="Üretici kodu", Position=7, Width=130, Desktop=false, Tablet=false, Mobile=false },
            new GridColumn { Field="specialCode1", Label="Özel kod 1", Position=8, Width=130, Desktop=false, Tablet=false, Mobile=false },
            new GridColumn { Field="specialCode2", Label="Özel kod 2", Position=9, Width=130, Desktop=false, Tablet=false, Mobile=false },
            new GridColumn { Field="description", Label="Açıklama", Position=10, Width=280, Desktop=false, Tablet=false, Mobile=false }
        );

        db.Banners.AddRange(
            new Banner { Title="Doğru teşhis. Güçlü servis.", Subtitle="Profesyonel diagnostik çözümlerini tek bir yerden keşfedin.", ButtonText="Cihazları incele", SearchTerm="Diagnostik", IsActive=true, Position=0 },
            new Banner { Title="Her bağlantıda güven.", Subtitle="Servisinizin ihtiyaç duyduğu kablo ve adaptörler.", ButtonText="Bağlantı ürünleri", SearchTerm="kablo", IsActive=true, Position=1 }
        );

        await db.SaveChangesAsync();

        db.StockMovements.AddRange(items.Select(product => new StockMovement
        {
            ProductId = product.Id,
            MovementType = "InitialBalance",
            QuantityDelta = 0,
            PreviousStock = product.Stock,
            NewStock = product.Stock,
            Reason = "Demo kataloğunun başlangıç bakiyesi."
        }));
        await db.SaveChangesAsync();
    }
}
