using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Endpoints;
using U1.Business.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
builder.Services.AddDbContextFactory<BusinessDbContext>((sp, options) =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("SqlServer")
        ?? throw new InvalidOperationException("SqlServer bağlantı dizesi bulunamadı.");
    options.UseSqlServer(connectionString);
});
builder.Services.AddSingleton<Database>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "U1.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var claim = context.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(claim, out var id))
            {
                context.RejectPrincipal();
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<BusinessDbContext>();
            var user = await db.Users
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new { x.IsActive, x.AuthVersion })
                .SingleOrDefaultAsync();

            if (
                user is null
                || !user.IsActive
                || user.AuthVersion.ToString() != context.Principal!.FindFirstValue("version")
            )
            {
                context.RejectPrincipal();
            }
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = TimeSpan.FromSeconds(1);
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadataRetryAfter))
            retryAfter = metadataRetryAfter;

        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                message = "Çok fazla istek gönderildi. Belirtilen süre sonra tekrar deneyin.",
                code = "RATE_LIMITED",
                retryAfterSeconds
            },
            cancellationToken: cancellationToken);
    };

    options.AddPolicy(
        "auth",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => RateWindow(builder.Configuration, "Auth", 15, 60)));

    options.AddPolicy(
        "catalog-search",
        context => RateLimitPartition.GetFixedWindowLimiter(
            UserPartition(context),
            _ => RateWindow(builder.Configuration, "CatalogSearch", 120, 60)));

    options.AddPolicy(
        "commerce-write",
        context => RateLimitPartition.GetFixedWindowLimiter(
            UserPartition(context),
            _ => RateWindow(builder.Configuration, "CommerceWrite", 60, 60)));

    options.AddPolicy(
        "image-upload",
        context => RateLimitPartition.GetFixedWindowLimiter(
            UserPartition(context),
            _ => RateWindow(builder.Configuration, "ImageUpload", 15, 60)));
});

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; "
        + "img-src 'self' https: data:; font-src 'self'; connect-src 'self'; "
        + "frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";

    try
    {
        await next();
    }
    catch (BusinessException ex)
    {
        context.Response.StatusCode = ex.Status;
        await context.Response.WriteAsJsonAsync(new { message = ex.Message, code = ex.Code });
    }
    catch (BadHttpRequestException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(
            new { message = "İstek gövdesi veya alan tipleri geçersiz." });
    }
    catch (AntiforgeryValidationException)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsJsonAsync(
            new { message = "Oturum doğrulanamadı. Sayfayı yenileyip tekrar deneyin." });
    }
    catch (DbUpdateException ex)
        when (ex.InnerException is SqlException { Number: 2601 or 2627 })
    {
        context.Response.StatusCode = 409;
        await context.Response.WriteAsJsonAsync(
            new { message = "Bu e-posta veya ürün kodu zaten kullanılıyor." });
    }
    catch (SqlException ex) when (ex.Number is 2601 or 2627)
    {
        context.Response.StatusCode = 409;
        await context.Response.WriteAsJsonAsync(
            new { message = "Bu e-posta veya ürün kodu zaten kullanılıyor." });
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "İstek işlenemedi");
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(
            new { message = "İşlem tamamlanamadı. Lütfen tekrar deneyin." });
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (
        context.Request.Path.StartsWithSegments("/api")
        && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method)
    )
    {
        await context.RequestServices
            .GetRequiredService<IAntiforgery>()
            .ValidateRequestAsync(context);
    }

    await next();
});

app.MapGet(
    "/api/csrf",
    (IAntiforgery antiforgery, HttpContext context) =>
        new { token = antiforgery.GetAndStoreTokens(context).RequestToken });

if (
    app.Environment.IsDevelopment()
    && Environment.GetEnvironmentVariable("U1_TEST_DATABASE") is { } testDatabase
)
{
    app.MapGet("/api/test-environment", () => new { database = testDatabase });
}

app.MapAuth();
app.MapCatalog();
app.MapCommerce();
app.MapAdmin();
app.MapFallbackToFile("index.html");
await app.Services.GetRequiredService<Database>().Initialize(app.Environment.IsDevelopment());
app.Run();

static FixedWindowRateLimiterOptions RateWindow(
    IConfiguration configuration,
    string name,
    int defaultPermitLimit,
    int defaultWindowSeconds)
{
    var permitLimit = Math.Max(
        1,
        configuration.GetValue<int?>($"RateLimits:{name}:PermitLimit") ?? defaultPermitLimit);
    var windowSeconds = Math.Max(
        1,
        configuration.GetValue<int?>($"RateLimits:{name}:WindowSeconds") ?? defaultWindowSeconds);

    return new FixedWindowRateLimiterOptions
    {
        PermitLimit = permitLimit,
        Window = TimeSpan.FromSeconds(windowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}

static string UserPartition(HttpContext context)
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!string.IsNullOrEmpty(userId))
        return "user:" + userId;

    return "anonymous:" + (context.Connection.RemoteIpAddress?.ToString() ?? "local");
}

public partial class Program
{
}
