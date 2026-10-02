using System.Data;
using Microsoft.EntityFrameworkCore;
using U1.Business.Data;
using U1.Business.Domain;
using U1.Business.Services;

namespace U1.Business.Endpoints;

public static partial class AdminEndpoints
{
    private static async Task<bool> HasCleanupAudit(BusinessDbContext db, string operationId, CancellationToken token) =>
        await db.AdminEvents.AsNoTracking().AnyAsync(x => x.EventType == "ImageCleanup"
            && x.EntityType == "UploadStorage" && x.Summary.Contains(" " + operationId + " işlemiyle"), token);

    private static async Task<HashSet<string>> ImageReferences(BusinessDbContext db, CancellationToken token) =>
        (await db.Products.AsNoTracking().Where(x => x.ImageUrl.StartsWith("/uploads/"))
            .Select(x => x.ImageUrl).ToListAsync(token)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void MapImageRecovery(RouteGroupBuilder api)
    {
        api.MapGet("/images/cleanup-pending", async (BusinessDbContext db, ProductImageStorage storage, HttpContext c) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, c.RequestAborted);
            await ImageReferenceGuard.AcquireAsync(db, c.RequestAborted);
            var references = await ImageReferences(db, c.RequestAborted);
            var batches = await storage.PendingCleanupAsync(c.RequestAborted);
            var items = new List<object>();
            foreach (var batch in batches)
            {
                var auditCommitted = await HasCleanupAudit(db, batch.OperationId, c.RequestAborted);
                var images = batch.Images.Select(x => new
                {
                    x.Url,
                    referenced = references.Contains(x.Url),
                    originalExists = File.Exists(x.OriginalPath) || Directory.Exists(x.OriginalPath)
                }).ToArray();
                items.Add(new
                {
                    batch.OperationId,
                    auditCommitted,
                    images,
                    canRestore = images.All(x => !x.originalExists),
                    canFinalize = auditCommitted && images.All(x => !x.referenced && !x.originalExists)
                });
            }
            await tx.CommitAsync(c.RequestAborted);
            return Results.Ok(new { items });
        });

        api.MapPost("/images/cleanup-pending/{operationId}/{action}", async (
            string operationId, string action, BusinessDbContext db, ProductImageStorage storage,
            ILogger<ProductImageStorage> logger, HttpContext c) =>
        {
            if (action is not ("restore" or "finalize"))
                throw new BusinessException("Geçersiz kurtarma eylemi.");
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, c.RequestAborted);
            await ImageReferenceGuard.AcquireAsync(db, c.RequestAborted);
            var batch = await storage.PendingCleanupAsync(operationId, c.RequestAborted);
            var references = await ImageReferences(db, c.RequestAborted);
            var auditCommitted = await HasCleanupAudit(db, operationId, c.RequestAborted);
            if (action == "finalize")
            {
                if (!auditCommitted)
                    throw new BusinessException("Temizlik geçmiş kaydı bulunamadı. Dosyaları geri koyun.", 409, "IMAGE_CLEANUP_UNCOMMITTED");
                if (batch.Images.Any(x => references.Contains(x.Url)))
                    throw new BusinessException("Kullanılan görsel korunuyor. Dosyaları geri koyun.", 409, "IMAGE_IN_USE");
                if (batch.Images.Any(x => File.Exists(x.OriginalPath) || Directory.Exists(x.OriginalPath)))
                    throw new BusinessException("Özgün dosya adı kullanılıyor. Dosyaları inceleyin.", 409, "IMAGE_RESTORE_CONFLICT");
            }

            // Commit the request before changing files. A crash leaves a truthful request
            // record and remaining files can be reconciled again under the same SQL lock.
            db.AdminEvents.Add(AuditTrail.Event(c.UserId(), "ImageCleanupRecoveryRequested", "UploadStorage", null,
                $"{operationId} işlemi için {(action == "restore" ? "geri koyma" : "sonlandırma")} istendi; {batch.Images.Count} dosya."));
            await db.SaveChangesAsync(c.RequestAborted);
            await tx.CommitAsync(c.RequestAborted);

            await using var recoveryTx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, c.RequestAborted);
            await ImageReferenceGuard.AcquireAsync(db, c.RequestAborted);
            batch = await storage.PendingCleanupAsync(operationId, c.RequestAborted);
            references = await ImageReferences(db, c.RequestAborted);
            if (action == "finalize" && !await HasCleanupAudit(db, operationId, c.RequestAborted))
                throw new BusinessException("Temizlik geçmiş kaydı bulunamadı. Dosyaları geri koyun.", 409, "IMAGE_CLEANUP_UNCOMMITTED");
            if (action == "finalize" && batch.Images.Any(x => references.Contains(x.Url)))
                throw new BusinessException("Kullanılan görsel korunuyor. Dosyaları geri koyun.", 409, "IMAGE_IN_USE");
            if (action == "finalize" && batch.Images.Any(x => File.Exists(x.OriginalPath) || Directory.Exists(x.OriginalPath)))
                throw new BusinessException("Özgün dosya adı kullanılıyor. Dosyaları inceleyin.", 409, "IMAGE_RESTORE_CONFLICT");
            var pending = 0;
            if (action == "restore")
                await storage.RestoreStagedAsync(batch, CancellationToken.None);
            else
                pending = await storage.FinalizeStagedAsync(batch, logger, CancellationToken.None);
            db.AdminEvents.Add(AuditTrail.Event(c.UserId(), "ImageCleanupRecovered", "UploadStorage", null,
                $"{operationId} işlemi: {(action == "restore" ? "geri koyuldu" : "sonlandırıldı")}; kalan {pending} dosya."));
            string? auditWarning = null;
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                await recoveryTx.CommitAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Files have already changed and cannot be rolled back by SQL. The
                // separately committed Requested event remains the durable evidence.
                logger.LogError(ex, "Görsel kurtarma sonucu geçmişe kaydedilemedi: {OperationId}", operationId);
                try
                {
                    await recoveryTx.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollbackError)
                {
                    logger.LogWarning(rollbackError, "Görsel kurtarma geçmiş transaction kapanışı başarısız: {OperationId}", operationId);
                }
                auditWarning = "Dosya işlemi uygulandı; sonuç geçmiş kaydı doğrulanamadı. İşlem kimliğiyle kontrol edin.";
            }
            return Results.Ok(new { operationId, pending, completed = pending == 0, auditWarning });
        }).RequireRateLimiting("image-upload");
    }
}
