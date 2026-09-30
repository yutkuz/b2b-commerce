using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
                            x.DealerGroupId,
                            DealerGroupName = db.DealerGroups
                                .Where(g => g.Id == x.DealerGroupId)
                                .Select(g => g.Name)
                                .FirstOrDefault(),
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
                if (u.Role == "Admin" && !input.IsActive && c.UserId() == id)
                    throw new BusinessException(
                        "Kendi yönetici hesabınızı bu ekrandan pasifleştiremezsiniz.",
                        409,
                        "ADMIN_SELF_DEACTIVATE"
                    );

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
                var dealerGroupId = u.Role == "Dealer"
                    ? input.DealerGroupId ?? u.DealerGroupId
                    : null;
                if (u.Role == "Dealer" && dealerGroupId is null)
                    throw new BusinessException("Bayi grubu gerekli.");
                if (dealerGroupId is not null
                    && !await db.DealerGroups.AsNoTracking().AnyAsync(x => x.Id == dealerGroupId))
                    throw new BusinessException("Geçerli bir bayi grubu seçin.");
                var version = input.Version;
                var target = db.Users.Where(x => x.Id == id && x.AuthVersion == version);
                await using var tx = await db.Database.BeginTransactionAsync();

                if (u.Role == "Admin" && u.IsActive && !isActive)
                {
                    var lockResult = await SqlApplicationLock.AcquireAsync(
                        (SqlConnection)db.Database.GetDbConnection(),
                        (SqlTransaction)tx.GetDbTransaction(),
                        "U1Business:active-admin-deactivation",
                        "Transaction",
                        10000
                    );
                    if (lockResult < 0)
                        throw new BusinessException(
                            "Yönetici hesabı şu anda güncellenemiyor. Lütfen tekrar deneyin.",
                            503,
                            "ADMIN_RETRY"
                        );

                    var activeAdmins = await db.Users.AsNoTracking()
                        .CountAsync(x => x.Role == "Admin" && x.IsActive);
                    if (activeAdmins <= 1)
                        throw new BusinessException(
                            "Son aktif yönetici hesabı pasifleştirilemez.",
                            409,
                            "LAST_ADMIN"
                        );
                }

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
                        .SetProperty(x => x.DealerGroupId, dealerGroupId)
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
                        .SetProperty(x => x.DealerGroupId, dealerGroupId)
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
                if (u.DealerGroupId != dealerGroupId)
                {
                    events.Add(AuditTrail.Event(
                        c.UserId(),
                        "DealerGroupAssigned",
                        "User",
                        id,
                        $"Kullanıcının bayi grubu {dealerGroupId} olarak güncellendi."));
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
