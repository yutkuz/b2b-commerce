using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static class AuthEndpoints
{
    public static int UserId(this HttpContext c) =>
        int.Parse(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static object PublicUser(User u) =>
        new
        {
            u.Id,
            u.FirstName,
            u.LastName,
            u.Email,
            u.Phone,
            u.Company,
            u.Role,
            u.IsActive,
        };

    private static Task SignIn(HttpContext c, User u) =>
        c.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
                        new Claim(ClaimTypes.Name, u.FirstName),
                        new Claim(ClaimTypes.Role, u.Role),
                        new Claim("version", u.AuthVersion.ToString()),
                    },
                    CookieAuthenticationDefaults.AuthenticationScheme
                )
            )
        );

    public static void MapAuth(this WebApplication app)
    {
        app.MapPost(
                "/api/auth/register",
                async (
                    RegisterInput input,
                    BusinessDbContext db,
                    IPasswordHasher<User> hasher,
                    HttpContext c
                ) =>
                {
                    if (input is null)
                        throw new BusinessException("Kayıt bilgileri gerekli.");
                    input.Company ??= "";
                    Rules.Validate(input);

                    await using var tx = await db.Database.BeginTransactionAsync();
                    var user = new User
                    {
                        FirstName = input.FirstName.Trim(),
                        LastName = input.LastName.Trim(),
                        Email = input.Email.Trim().ToLowerInvariant(),
                        Phone = input.Phone,
                        Company = input.Company.Trim(),
                        Role = "Dealer",
                        IsActive = true,
                        AuthVersion = 1,
                    };
                    user.PasswordHash = hasher.HashPassword(user, input.Password);

                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                    db.Carts.Add(new Cart { UserId = user.Id });
                    await db.SaveChangesAsync();
                    await tx.CommitAsync();

                    await SignIn(c, user);
                    return Results.Ok(PublicUser(user));
                }
            )
            .RequireRateLimiting("auth");

        app.MapPost(
                "/api/auth/login",
                async (
                    LoginInput input,
                    BusinessDbContext db,
                    IPasswordHasher<User> hasher,
                    HttpContext c
                ) =>
                {
                    if (input is null)
                        throw new BusinessException("E-posta ve şifrenizi kontrol edin.", 401);
                    if (
                        string.IsNullOrWhiteSpace(input.Email)
                        || string.IsNullOrEmpty(input.Password)
                        || input.Password.Length > 128
                    )
                        throw new BusinessException("E-posta ve şifrenizi kontrol edin.", 401);

                    var email = input.Email.Trim().ToLowerInvariant();
                    var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email);
                    if (
                        u is null
                        || !u.IsActive
                        || hasher.VerifyHashedPassword(u, u.PasswordHash, input.Password)
                            == PasswordVerificationResult.Failed
                    )
                        throw new BusinessException("E-posta veya şifre hatalı.", 401);

                    await SignIn(c, u);
                    return Results.Ok(PublicUser(u));
                }
            )
            .RequireRateLimiting("auth");

        app.MapPost(
                "/api/auth/logout",
                async (HttpContext c) =>
                {
                    await c.SignOutAsync();
                    return Results.Ok();
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/auth/me",
                async (BusinessDbContext db, HttpContext c) =>
                {
                    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.UserId())
                        ?? throw new BusinessException("Kullanıcı bulunamadı.", 404);
                    return PublicUser(user);
                }
            )
            .RequireAuthorization();
    }
}
