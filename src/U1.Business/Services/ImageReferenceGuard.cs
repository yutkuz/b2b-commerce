using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using U1.Business.Data;
using U1.Business.Domain;

namespace U1.Business.Services;

internal static class ImageReferenceGuard
{
    private const string Resource = "U1Business:product-image-references";

    public static async Task AcquireAsync(BusinessDbContext db, CancellationToken cancellationToken)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Görsel referans kilidi için transaction gerekli.");
        var result = await SqlApplicationLock.AcquireAsync(
            (SqlConnection)db.Database.GetDbConnection(),
            (SqlTransaction)transaction.GetDbTransaction(),
            Resource,
            "Transaction",
            10000);
        if (result < 0)
            throw new BusinessException(
                "Görsel referansları şu anda güncellenemiyor. Lütfen tekrar deneyin.",
                503,
                "IMAGE_REFERENCE_RETRY");
    }
}
