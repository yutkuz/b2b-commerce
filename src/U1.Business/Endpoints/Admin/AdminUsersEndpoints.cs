using Dapper;
using Microsoft.AspNetCore.Identity;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static void MapUsers(RouteGroupBuilder api)
    {
        api.MapGet(
            "/users",
            async (string? q, int? page, Database database) =>
            {
                using var db = database.Open();
                var args = new
                {
                    search = "%" + (q ?? "") + "%",
                    offset = (Math.Clamp(page ?? 1, 1, 100000) - 1) * 20,
                };
                const string where =
                    " FROM Users WHERE FirstName LIKE @search OR LastName LIKE @search OR Email LIKE @search OR Company LIKE @search";
                return new
                {
                    items = await db.QueryAsync(
                        "SELECT Id,FirstName,LastName,Email,Phone,Company,Role,IsActive,AuthVersion AS Version"
                            + where
                            + " ORDER BY Id DESC OFFSET @offset ROWS FETCH NEXT 20 ROWS ONLY",
                        args
                    ),
                    total = await db.ExecuteScalarAsync<int>("SELECT COUNT(*)" + where, args),
                };
            }
        );
        api.MapPut(
            "/users/{id:int}",
            async (
                int id,
                UserInput input,
                Database database,
                IPasswordHasher<User> hasher,
                HttpContext c
            ) =>
            {
                if (input is null)
                    throw new BusinessException("Kullanıcı bilgileri gerekli.");
                input.Company ??= "";
                Rules.Validate(input);
                using var db = database.Open();
                var u =
                    await db.QuerySingleOrDefaultAsync<User>(
                        "SELECT * FROM Users WHERE Id=@id",
                        new { id }
                    ) ?? throw new BusinessException("Kullanıcı bulunamadı.", 404);
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
                var args = new
                {
                    id,
                    input.FirstName,
                    input.LastName,
                    Email = input.Email.Trim().ToLowerInvariant(),
                    input.Phone,
                    input.Company,
                    input.IsActive,
                    input.Version,
                    passwordHash,
                };
                var sql = passwordHash is null
                    ? "UPDATE Users SET FirstName=@FirstName,LastName=@LastName,Email=@Email,Phone=@Phone,Company=@Company,IsActive=@IsActive,AuthVersion=AuthVersion+1 WHERE Id=@id AND AuthVersion=@Version"
                    : "UPDATE Users SET FirstName=@FirstName,LastName=@LastName,Email=@Email,Phone=@Phone,Company=@Company,IsActive=@IsActive,PasswordHash=@passwordHash,AuthVersion=AuthVersion+1 WHERE Id=@id AND AuthVersion=@Version";
                if (await db.ExecuteAsync(sql, args) == 0)
                {
                    if (
                        await db.ExecuteScalarAsync<int>(
                            "SELECT COUNT(*) FROM Users WHERE Id=@id",
                            new { id }
                        ) == 0
                    )
                        throw new BusinessException("Kullanıcı bulunamadı.", 404);
                    throw new BusinessException(
                        "Kullanıcı başka bir işlemde değişti. Güncel bilgileri yeniden yükleyip tekrar deneyin.",
                        409,
                        "USER_CHANGED"
                    );
                }
                return Results.Ok();
            }
        );
    }
}
