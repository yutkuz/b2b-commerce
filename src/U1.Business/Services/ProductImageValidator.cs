using SkiaSharp;
using U1.Business.Domain;

namespace U1.Business.Services;

public static class ProductImageValidator
{
    public const int MaxDimension = 4096;
    public const long MaxPixels = 12_000_000;

    public static string Validate(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            throw InvalidImage();

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
            throw InvalidImage();

        var source = codec.Info;
        if (source.Width <= 0 || source.Height <= 0)
            throw InvalidImage();

        var pixels = (long)source.Width * source.Height;
        if (
            source.Width > MaxDimension
            || source.Height > MaxDimension
            || pixels > MaxPixels
        )
        {
            throw new BusinessException(
                $"Görsel en fazla {MaxDimension}×{MaxDimension} piksel ve 12 megapiksel olabilir."
            );
        }

        var extension = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Png => ".png",
            SKEncodedImageFormat.Jpeg => ".jpg",
            SKEncodedImageFormat.Webp => ".webp",
            _ => throw new BusinessException(
                "Yalnızca PNG, JPEG ve WebP dosyaları kabul edilir."
            )
        };

        var decodedInfo = new SKImageInfo(
            source.Width,
            source.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var bitmap = new SKBitmap(decodedInfo);
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result != SKCodecResult.Success)
            throw InvalidImage();

        return extension;
    }

    private static BusinessException InvalidImage() =>
        new("Görsel çözümlenemedi. Geçerli bir PNG, JPEG veya WebP dosyası seçin.");
}
