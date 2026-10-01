using Microsoft.Extensions.Configuration;
using U1.Business.Domain;

namespace U1.Business.Services;

public sealed record ImageCleanupCandidate(
    string Url,
    long SizeBytes,
    DateTime LastWriteUtc,
    DateTime DeleteAfterUtc,
    bool CanDelete);

internal sealed record StagedImage(string Url, string OriginalPath, string StagedPath);

internal sealed record ImageCleanupBatch(string OperationId, IReadOnlyList<StagedImage> Images)
{
    public IReadOnlyList<string> Urls => Images.Select(x => x.Url).ToArray();
}

public sealed class ProductImageStorage
{
    private static readonly string[] ManagedExtensions = [".png", ".jpg", ".jpeg", ".webp"];
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string webRoot;
    private readonly string uploadRoot;
    private readonly long totalQuotaBytes;
    private readonly long minimumFreeSpaceBytes;
    private readonly TimeSpan orphanGrace;

    public ProductImageStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(environment.WebRootPath))
            throw new InvalidOperationException("Web kök dizini bulunamadı.");

        webRoot = Path.GetFullPath(environment.WebRootPath);
        uploadRoot = Path.GetFullPath(Path.Combine(webRoot, "uploads"));
        totalQuotaBytes = Math.Max(
            1,
            configuration.GetValue<long?>("ImageStorage:TotalQuotaBytes") ?? 100L * 1024 * 1024);
        minimumFreeSpaceBytes = Math.Max(
            0,
            configuration.GetValue<long?>("ImageStorage:MinimumFreeSpaceBytes") ?? 100L * 1024 * 1024);
        orphanGrace = TimeSpan.FromMinutes(Math.Max(
            0,
            configuration.GetValue<int?>("ImageStorage:OrphanGraceMinutes") ?? 60));
        CleanupBatchLimit = Math.Clamp(
            configuration.GetValue<int?>("ImageStorage:CleanupBatchLimit") ?? 100,
            1,
            500);
    }

    public int CleanupBatchLimit { get; }

    public async Task EnsureManagedReferenceExistsAsync(string url, CancellationToken cancellationToken)
    {
        if (!url.StartsWith("/uploads/", StringComparison.Ordinal))
            return;

        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStorageRoot();
            ResolveManagedPath(url, requireExisting: true);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> SaveAsync(
        byte[] bytes,
        string extension,
        CancellationToken cancellationToken)
    {
        if (bytes is null || bytes.Length == 0 || !IsManagedExtension(extension))
            throw new BusinessException("Geçerli bir ürün görseli gerekli.");

        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStorageRoot();
            var usedBytes = StoredBytes();
            if (bytes.LongLength > totalQuotaBytes
                || usedBytes > totalQuotaBytes - bytes.LongLength)
            {
                throw new BusinessException(
                    "Görsel depolama kotası dolu. Kullanılmayan görselleri önizleyip temizleyin.",
                    409,
                    "IMAGE_STORAGE_QUOTA");
            }

            var driveRoot = Path.GetPathRoot(uploadRoot)
                ?? throw new InvalidOperationException("Görsel depolama sürücüsü bulunamadı.");
            var available = new DriveInfo(driveRoot).AvailableFreeSpace;
            if (available < minimumFreeSpaceBytes
                || bytes.LongLength > available - minimumFreeSpaceBytes)
            {
                throw new BusinessException(
                    "Sunucuda yeni görsel için yeterli güvenli boş alan yok.",
                    503,
                    "IMAGE_STORAGE_LOW_SPACE");
            }

            var name = Guid.NewGuid().ToString("N") + extension.ToLowerInvariant();
            var finalPath = ResolveManagedPath("/uploads/" + name, requireExisting: false);
            var temporaryPath = finalPath + ".uploading";
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
                File.Move(temporaryPath, finalPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }

            return "/uploads/" + name;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<ImageCleanupCandidate>> PreviewCleanupAsync(
        ISet<string> referencedUrls,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStorageRoot();
            var now = DateTime.UtcNow;
            return SafeFiles()
                .Where(file => IsManagedExtension(file.Extension))
                .Select(file =>
                {
                    var url = "/uploads/" + file.Name;
                    var deleteAfter = file.LastWriteTimeUtc + orphanGrace;
                    return new ImageCleanupCandidate(
                        url,
                        file.Length,
                        file.LastWriteTimeUtc,
                        deleteAfter,
                        deleteAfter <= now);
                })
                .Where(item => !referencedUrls.Contains(item.Url))
                .OrderBy(item => item.LastWriteUtc)
                .ThenBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<ImageCleanupBatch> StageUnusedAsync(
        IEnumerable<string> urls,
        ISet<string> referencedUrls,
        CancellationToken cancellationToken)
    {
        var selected = urls
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0)
            throw new BusinessException("Temizlenecek en az bir görsel seçin.");
        if (selected.Length > CleanupBatchLimit)
            throw new BusinessException(
                $"Tek işlemde en fazla {CleanupBatchLimit} görsel temizlenebilir.");

        await gate.WaitAsync(cancellationToken);
        try
        {
            EnsureStorageRoot();
            var now = DateTime.UtcNow;
            var paths = new List<(string Url, string Path)>(selected.Length);

            foreach (var url in selected)
            {
                if (referencedUrls.Contains(url))
                {
                    throw new BusinessException(
                        "Seçilen görsellerden biri bir ürün tarafından kullanılıyor.",
                        409,
                        "IMAGE_IN_USE");
                }

                var path = ResolveManagedPath(url, requireExisting: true);
                var file = new FileInfo(path);
                if (file.LastWriteTimeUtc + orphanGrace > now)
                {
                    throw new BusinessException(
                        "Yeni yüklenen kullanılmayan görseller bekleme süresi dolmadan silinemez.",
                        409,
                        "IMAGE_GRACE_PERIOD");
                }

                paths.Add((url, path));
            }

            var operationId = Guid.NewGuid().ToString("N");
            var stagingRoot = Path.Combine(uploadRoot, ".pending-cleanup");
            var stagingFolder = Path.Combine(stagingRoot, operationId);
            Directory.CreateDirectory(stagingRoot);
            EnsureNoReparsePoint(stagingRoot);
            Directory.CreateDirectory(stagingFolder);
            EnsureNoReparsePoint(stagingFolder);
            var staged = new List<StagedImage>(paths.Count);
            try
            {
                foreach (var item in paths)
                {
                    var stagedPath = Path.Combine(stagingFolder, Path.GetFileName(item.Path));
                    File.Move(item.Path, stagedPath);
                    staged.Add(new StagedImage(item.Url, item.Path, stagedPath));
                }
            }
            catch
            {
                foreach (var item in staged)
                    File.Move(item.StagedPath, item.OriginalPath);
                RemoveEmptyStagingFolder(stagingFolder);
                throw;
            }

            return new ImageCleanupBatch(operationId, staged);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task RestoreStagedAsync(
        ImageCleanupBatch batch,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var image in batch.Images)
                File.Move(image.StagedPath, image.OriginalPath);
            if (batch.Images.Count > 0)
                RemoveEmptyStagingFolder(Path.GetDirectoryName(batch.Images[0].StagedPath)!);
        }
        finally
        {
            gate.Release();
        }
    }

    internal async Task<int> FinalizeStagedAsync(
        ImageCleanupBatch batch,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var pending = 0;
            foreach (var image in batch.Images)
            {
                try
                {
                    File.Delete(image.StagedPath);
                }
                catch (IOException ex)
                {
                    pending++;
                    logger.LogWarning(ex, "Görsel temizliği beklemede: {OperationId}", batch.OperationId);
                }
                catch (UnauthorizedAccessException ex)
                {
                    pending++;
                    logger.LogWarning(ex, "Görsel temizliği beklemede: {OperationId}", batch.OperationId);
                }
            }
            if (batch.Images.Count > 0)
                RemoveEmptyStagingFolder(Path.GetDirectoryName(batch.Images[0].StagedPath)!);
            return pending;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void RemoveEmptyStagingFolder(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            Directory.Delete(folder);
    }

    private void EnsureStorageRoot()
    {
        Directory.CreateDirectory(uploadRoot);
        EnsureNoReparsePoint(webRoot);
        EnsureNoReparsePoint(uploadRoot);
    }

    private IEnumerable<FileInfo> SafeFiles()
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(
                     uploadRoot,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new BusinessException(
                    "Görsel depolama alanında güvenli olmayan bir bağlantı bulundu.",
                    503,
                    "IMAGE_STORAGE_UNSAFE");
            }

            if ((attributes & FileAttributes.Directory) != 0)
                continue;

            yield return new FileInfo(path);
        }
    }

    private long StoredBytes()
    {
        var used = SafeFiles().Sum(x => x.Length);
        var stagingRoot = Path.Combine(uploadRoot, ".pending-cleanup");
        if (!Directory.Exists(stagingRoot))
            return used;

        EnsureNoReparsePoint(stagingRoot);
        foreach (var folder in Directory.EnumerateFileSystemEntries(stagingRoot))
        {
            var attributes = File.GetAttributes(folder);
            if ((attributes & FileAttributes.ReparsePoint) != 0
                || (attributes & FileAttributes.Directory) == 0)
                throw UnsafeStorage();

            foreach (var file in Directory.EnumerateFileSystemEntries(folder))
            {
                attributes = File.GetAttributes(file);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                    throw UnsafeStorage();
                used = checked(used + new FileInfo(file).Length);
            }
        }

        return used;
    }

    private string ResolveManagedPath(string url, bool requireExisting)
    {
        const string prefix = "/uploads/";
        if (!url.StartsWith(prefix, StringComparison.Ordinal)
            || url.Length <= prefix.Length)
        {
            throw InvalidPath();
        }

        var name = url[prefix.Length..];
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal)
            || name is "." or ".."
            || !IsManagedExtension(Path.GetExtension(name)))
        {
            throw InvalidPath();
        }

        var fullPath = Path.GetFullPath(Path.Combine(uploadRoot, name));
        var expectedPrefix = uploadRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
            throw InvalidPath();

        if (requireExisting)
        {
            if (!File.Exists(fullPath))
            {
                throw new BusinessException(
                    "Seçilen görsel artık bulunamıyor. Önizlemeyi yenileyin.",
                    409,
                    "IMAGE_CHANGED");
            }

            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw InvalidPath();
        }

        return fullPath;
    }

    private static void EnsureNoReparsePoint(string path)
    {
        DirectoryInfo? current = new(path);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new BusinessException(
                    "Görsel depolama yolu güvenli olmayan bir bağlantı içeriyor.",
                    503,
                    "IMAGE_STORAGE_UNSAFE");
            }

            current = current.Parent;
        }
    }

    private static bool IsManagedExtension(string extension) =>
        ManagedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);

    private static BusinessException InvalidPath() =>
        new(
            "Yalnızca uploads klasöründeki doğrulanmış ürün görselleri temizlenebilir.",
            400,
            "IMAGE_PATH_INVALID");

    private static BusinessException UnsafeStorage() =>
        new(
            "Görsel depolama alanında güvenli olmayan bir bağlantı veya dosya bulundu.",
            503,
            "IMAGE_STORAGE_UNSAFE");
}
