using Microsoft.EntityFrameworkCore;
using U1.Business.Domain;

namespace U1.Business.Data;

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();
    public DbSet<GridColumn> GridColumns => Set<GridColumn>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<SchemaVersion> SchemaVersions => Set<SchemaVersion>();
    public DbSet<DemoSetup> DemoSetup => Set<DemoSetup>();
    public DbSet<AdminEvent> AdminEvents => Set<AdminEvent>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Data/*.sql and SchemaVersions are the authoritative schema-migration mechanism.
    // EF Core owns runtime mapping/querying; keep this model aligned with the SQL schema.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FirstName).HasMaxLength(80);
            entity.Property(x => x.LastName).HasMaxLength(80);
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Phone).HasMaxLength(25);
            entity.Property(x => x.Company).HasMaxLength(180);
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.Role).HasMaxLength(12).IsUnicode(false);
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(80);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(60);
            entity.Property(x => x.Name).HasMaxLength(180);
            entity.Property(x => x.Description).HasMaxLength(3000);
            entity.Property(x => x.Brand).HasMaxLength(80);
            entity.Property(x => x.ManufacturerCode).HasMaxLength(80);
            entity.Property(x => x.SpecialCode1).HasMaxLength(80);
            entity.Property(x => x.SpecialCode2).HasMaxLength(80);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.ArchiveReason).HasMaxLength(300).HasDefaultValue("");
            entity.Property(x => x.IsArchived).HasDefaultValue(false);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Code).IsUnique();
            entity.HasIndex(x => new { x.CategoryId, x.Brand });
            entity.HasIndex(x => new { x.IsArchived, x.CategoryId, x.Brand });
            entity.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ArchivedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Cart>(entity =>
        {
            entity.ToTable("Carts");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CartItem>(entity =>
        {
            entity.ToTable("CartItems");
            entity.HasKey(x => new { x.CartId, x.ProductId });
            entity.HasOne<Cart>().WithMany().HasForeignKey(x => x.CartId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Number).HasMaxLength(40).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(20).HasDefaultValue("Bekliyor");
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.AdminNote).HasMaxLength(1000);
            entity.Property(x => x.RejectionReason).HasMaxLength(300);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.HasIndex(x => x.Number).IsUnique();
            entity.HasIndex(x => x.RequestId).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.CreatedAt });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItems");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductCode).HasMaxLength(60);
            entity.Property(x => x.ProductName).HasMaxLength(180);
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.Property(x => x.Total).HasPrecision(18, 2);
            entity.HasIndex(x => x.OrderId);
            entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<OrderStatusHistory>(entity =>
        {
            entity.ToTable("OrderStatusHistory");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FromStatus).HasMaxLength(20);
            entity.Property(x => x.ToStatus).HasMaxLength(20);
            entity.Property(x => x.Reason).HasMaxLength(300);
            entity.Property(x => x.ChangedAt).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.OrderId, x.ChangedAt, x.Id });
            entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<GridColumn>(entity =>
        {
            entity.ToTable("GridColumns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Field).HasMaxLength(50).IsUnicode(false);
            entity.Property(x => x.Label).HasMaxLength(60);
            entity.Property(x => x.RenderType).HasMaxLength(20).IsUnicode(false);
            entity.Property(x => x.Align).HasMaxLength(10).IsUnicode(false);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => x.Field).IsUnique();
        });

        modelBuilder.Entity<Banner>(entity =>
        {
            entity.ToTable("Banners");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(100);
            entity.Property(x => x.Subtitle).HasMaxLength(300);
            entity.Property(x => x.ButtonText).HasMaxLength(40);
            entity.Property(x => x.SearchTerm).HasMaxLength(100);
            entity.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<SchemaVersion>(entity =>
        {
            entity.ToTable("SchemaVersions");
            entity.HasKey(x => x.Version);
        });

        modelBuilder.Entity<DemoSetup>(entity =>
        {
            entity.ToTable("DemoSetup");
            entity.HasKey(x => x.Component);
            entity.Property(x => x.Component).HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Status).HasMaxLength(16).IsUnicode(false);
        });

        modelBuilder.Entity<AdminEvent>(entity =>
        {
            entity.ToTable("AdminEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EventType).HasMaxLength(40).IsUnicode(false);
            entity.Property(x => x.EntityType).HasMaxLength(30).IsUnicode(false);
            entity.Property(x => x.Summary).HasMaxLength(500);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.EntityType, x.EntityId, x.CreatedAt });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MovementType).HasMaxLength(40).IsUnicode(false);
            entity.Property(x => x.Reason).HasMaxLength(300);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()").ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.ProductId, x.CreatedAt, x.Id });
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.OrderId, x.ProductId, x.MovementType })
                .IsUnique()
                .HasFilter("[OrderId] IS NOT NULL");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<AdminEvent>().WithMany().HasForeignKey(x => x.AdminEventId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}
