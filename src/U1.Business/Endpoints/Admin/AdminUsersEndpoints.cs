using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet(
            "/users",
            async (string? q, int? page, BusinessDbContext db) =>
            {
                var search = q ?? "";
                var offset = (Math.Clamp(page ?? 1, 1, 100000) - 1) * 20;
                var query = db.Users
                    .AsNoTracking()
                    .Where(x =>
                        x.FirstName.Contains(search)
                        || x.LastName.Contains(search)
                        || x.Email.Contains(search)
                        || x.Company.Contains(search));

                return new
                {
                    items = await query
                        .OrderByDescending(x => x.Id)
                        .Skip(offset)
                        .Take(20)
                        .Select(x => new
                        {
                            x.Id,
                            x.FirstName,
                            x.LastName,
                            x.Email,
                            x.Phone,
                            x.Company,
                            x.Role,
                            x.IsActive,
                            Version = x.AuthVersion
                        })
                        .ToListAsync(),
                    total = await query.CountAsync(),
                };
            }
        );

        api.MapPut(
            "/users/{id:int}",
            async (
                int id,
                UserInput input,
                BusinessDbContext db,
                IPasswordHasher<User> hasher,
                HttpContext c
            ) =>
            {
                if (input is null)
                    throw new BusinessException("Kullanıcı bilgileri gerekli.");
                input.Company ??= "";
                Rules.Validate(input);

                var u = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id)
                    ?? throw new BusinessException("Kullanıcı bulunamadı.", 404);

                if (input.Version <= 0)
                    throw new BusinessException("Kullanıcı sürümü gerekli.");
                if (u.Role == "Admin" && !input.IsActive)
                    throw new BusinessException("Yönetici hesabı bu ekrandan pasifleştirilemez.");

                string? passwordHash = null;
                if (!string.IsNullOrEmpty(input.NewPassword))
                {
                    if (input.NewPassword.Length < 10 || input.NewPassword.Length > 128)
                        throw new BusinessException("Yeni şifre 10–128 karakter olmalı.");
                    passwordHash = hasher.HashPassword(u, input.NewPassword);
                }

                var firstName = input.FirstName;
                var lastName = input.LastName;
                var email = input.Email.Trim().ToLowerInvariant();
                var phone = input.Phone;
                var company = input.Company;
                var isActive = input.IsActive;
                var version = input.Version;
                var target = db.Users.Where(x => x.Id == id && x.AuthVersion == version);
                await using var tx = await db.Database.BeginTransactionAsync();

                int updated;
                if (passwordHash is null)
                {
                    updated = await target.ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.FirstName, firstName)
                        .SetProperty(x => x.LastName, lastName)
                        .SetProperty(x => x.Email, email)
                        .SetProperty(x => x.Phone, phone)
                        .SetProperty(x => x.Company, company)
                        .SetProperty(x => x.IsActive, isActive)
                        .SetProperty(x => x.AuthVersion, x => x.AuthVersion + 1));
                }
                else
                {
                    updated = await target.ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.FirstName, firstName)
                        .SetProperty(x => x.LastName, lastName)
                        .SetProperty(x => x.Email, email)
                        .SetProperty(x => x.Phone, phone)
                        .SetProperty(x => x.Company, company)
                        .SetProperty(x => x.IsActive, isActive)
                        .SetProperty(x => x.PasswordHash, passwordHash)
                        .SetProperty(x => x.AuthVersion, x => x.AuthVersion + 1));
                }

                if (updated == 0)
                {
                    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id))
                        throw new BusinessException("Kullanıcı bulunamadı.", 404);

                    throw new BusinessException(
                        "Kullanıcı başka bir işlemde değişti. Güncel bilgileri yeniden yükleyip tekrar deneyin.",
                        409,
                        "USER_CHANGED"
                    );
                }

                var events = new List<AdminEvent>();
                if (u.IsActive != isActive)
                {
                    events.Add(AuditTrail.Event(
                        c.UserId(),
                        "UserStatusChanged",
                        "User",
                        id,
                        $"Kullanıcı aktifliği {u.IsActive} değerinden {isActive} değerine değiştirildi."));
                }
                if (passwordHash is not null)
                {
                    events.Add(AuditTrail.Event(
                        c.UserId(),
                        "UserPasswordReset",
                        "User",
                        id,
                        "Kullanıcı parolası sıfırlandı."));
                }
                if (
                    u.FirstName != firstName
                    || u.LastName != lastName
                    || u.Email != email
                    || u.Phone != phone
                    || u.Company != company
                )
                {
                    events.Add(AuditTrail.Event(
                        c.UserId(),
                        "UserUpdated",
                        "User",
                        id,
                        "Kullanıcı profil bilgileri güncellendi."));
                }
                if (events.Count == 0)
                {
                    events.Add(AuditTrail.Event(
                        c.UserId(),
                        "UserUpdated",
                        "User",
                        id,
                        "Kullanıcı kaydı yeniden kaydedildi."));
                }

                db.AdminEvents.AddRange(events);
                await db.SaveChangesAsync();
                await tx.CommitAsync();

                return Results.Ok();
            }
        );
    }
}
