using System.Security.Cryptography;

namespace Trazo.Infrastructure.Seeding;

/// <summary>Validation shared by every way an exercise image can enter the catalog (seed, upload, transfer).</summary>
public static class ExerciseImageRules
{
    public const int MaximumBytes = 1024 * 1024;
    public const int MaximumSlot = 3;

    /// <summary>Detects the format from the file signature; the declared content type is never trusted.</summary>
    public static bool TryDetectContentType(ReadOnlySpan<byte> data, out string contentType)
    {
        contentType = "";
        if (data.Length < 12) return false;
        if (data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) contentType = "image/webp";
        else if (data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) contentType = "image/png";
        else if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) contentType = "image/jpeg";
        return contentType.Length > 0;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Extension(string contentType) => contentType switch
    {
        "image/webp" => "webp",
        "image/png" => "png",
        "image/jpeg" => "jpg",
        _ => throw new InvalidDataException($"Unsupported image type '{contentType}'.")
    };
}
