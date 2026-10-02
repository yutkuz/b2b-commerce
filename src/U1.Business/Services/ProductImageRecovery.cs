using U1.Business.Domain;

namespace U1.Business.Services;

public sealed partial class ProductImageStorage
{
    internal async Task<IReadOnlyList<ImageCleanupBatch>> PendingCleanupAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            EnsureStorageRoot();
            var root = Path.Combine(uploadRoot, ".pending-cleanup");
            if (!Directory.Exists(root))
                return [];
            EnsureNoReparsePoint(root);
            return Directory.EnumerateFileSystemEntries(root)
                .Select(ReadPendingBatch).ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<ImageCleanupBatch> PendingCleanupAsync(string operationId, CancellationToken token)
    {
        if (!Guid.TryParseExact(operationId, "N", out _))
            throw InvalidPath();
        await gate.WaitAsync(token);
        try
        {
            EnsureStorageRoot();
            var root = Path.Combine(uploadRoot, ".pending-cleanup");
            if (!Directory.Exists(root))
                throw new BusinessException("Bekleyen temizlik bulunamadı. Listeyi yenileyin.", 409, "IMAGE_CHANGED");
            EnsureNoReparsePoint(root);
            var path = Path.Combine(root, operationId);
            if (!Directory.Exists(path))
                throw new BusinessException("Bekleyen temizlik bulunamadı. Listeyi yenileyin.", 409, "IMAGE_CHANGED");
            return ReadPendingBatch(path);
        }
        finally
        {
            gate.Release();
        }
    }

    private ImageCleanupBatch ReadPendingBatch(string folder)
    {
        var operationId = Path.GetFileName(folder);
        if (!Guid.TryParseExact(operationId, "N", out _)
            || (File.GetAttributes(folder) & FileAttributes.Directory) == 0)
            throw UnsafeStorage();
        EnsureNoReparsePoint(folder);
        var images = new List<StagedImage>();
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw UnsafeStorage();
            var url = "/uploads/" + Path.GetFileName(path);
            var original = ResolveManagedPath(url, requireExisting: false);
            images.Add(new StagedImage(url, original, path));
        }
        return new ImageCleanupBatch(operationId, images);
    }
}
