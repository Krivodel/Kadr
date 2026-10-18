using System.Security.Cryptography;
using System.Text;

using NSec.Cryptography;

namespace Kadr.Desktop.Services.Updates;

public static class UpdateSignature
{
    public static byte[] GetMessage(string version, string assetName, string sha256)
    {
        if (!System.Version.TryParse(version, out _) || string.IsNullOrWhiteSpace(assetName) || Path.GetFileName(assetName) != assetName
            || assetName.IndexOfAny(new char[] { '\r', '\n', '\\', '/', '\0' }) >= 0
            || sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("Invalid update signature metadata.");
        }
        return Encoding.UTF8.GetBytes($"Kadr update v1\n{version}\n{assetName}\n{sha256.ToLowerInvariant()}\n");
    }

    public static bool Verify(UpdateSignatureManifest manifest, string version, string assetName, string sha256, ReadOnlySpan<byte> publicKey)
    {
        if (manifest.Version != version || manifest.AssetName != assetName
            || !string.Equals(manifest.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        try
        {
            PublicKey key = PublicKey.Import(SignatureAlgorithm.Ed25519, publicKey, KeyBlobFormat.RawPublicKey);
            return SignatureAlgorithm.Ed25519.Verify(key, GetMessage(version, assetName, sha256), Convert.FromBase64String(manifest.Signature));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }
}
