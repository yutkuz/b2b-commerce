using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;
using U1.Business.Endpoints;

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
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SameSite = SameSiteMode.Strict; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "U1.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async c =>
    {
        var claim = c.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(claim, out var id))
        {
            c.RejectPrincipal();
            return;
        }

        var db = c.HttpContext.RequestServices.GetRequiredService<BusinessDbContext>();
        var u = await db.Users
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.IsActive, x.AuthVersion })
            .SingleOrDefaultAsync();

        if (u is null || !u.IsActive || u.AuthVersion.ToString() != c.Principal!.FindFirstValue("version"))
            c.RejectPrincipal();
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("Admin", p => p.RequireRole("Admin")));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1) }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    if (context.Request.Path.StartsWithSegments("/api")) context.Response.Headers.CacheControl = "no-store";
    try { await next(); }
    catch (BusinessException ex) { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { message = ex.Message, code = ex.Code }); }
    catch (BadHttpRequestException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "İstek gövdesi veya alan tipleri geçersiz." }); }
    catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { message = "Oturum doğrulanamadı. Sayfayı yenileyip tekrar deneyin." }); }
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = "Bu e-posta veya ürün kodu zaten kullanılıyor." }); }
    catch (SqlException ex) when (ex.Number is 2601 or 2627) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = "Bu e-posta veya ürün kodu zaten kullanılıyor." }); }
    catch (Exception ex) { app.Logger.LogError(ex, "İstek işlenemedi"); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "İşlem tamamlanamadı. Lütfen tekrar deneyin." }); }
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapGet("/api/csrf", (IAntiforgery anti, HttpContext c) => new { token = anti.GetAndStoreTokens(c).RequestToken });
if (app.Environment.IsDevelopment() && Environment.GetEnvironmentVariable("U1_TEST_DATABASE") is { } testDatabase)
    app.MapGet("/api/test-environment", () => new { database = testDatabase });
app.MapAuth();
app.MapCatalog();
app.MapCommerce();
app.MapAdmin();
app.MapFallbackToFile("index.html");
await app.Services.GetRequiredService<Database>().Initialize(app.Environment.IsDevelopment());
app.Run();

public partial class Program { }
