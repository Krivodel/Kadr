using System.Security.Cryptography;

using Kadr.Desktop.Services.Updates;
using NSec.Cryptography;
using Xunit;

namespace Kadr.Desktop.Tests;

public sealed class UpdateSignatureTests
{
    [Theory]
    [InlineData("1.1.0", "Kadr-win-x64.exe", true)]
    [InlineData("1.2.0", "Kadr-win-x64.exe", false)]
    [InlineData("1.1.0", "Other.exe", false)]
    public void Verify_SignedManifest_BindsVersionAndAsset(string version, string asset, bool expected)
    {
        using Key key = Key.Create(SignatureAlgorithm.Ed25519);
        string hash = Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3 }));
        byte[] signature = SignatureAlgorithm.Ed25519.Sign(key, UpdateSignature.GetMessage("1.1.0", "Kadr-win-x64.exe", hash));
        UpdateSignatureManifest manifest = new("1.1.0", "Kadr-win-x64.exe", hash, Convert.ToBase64String(signature));

        bool verified = UpdateSignature.Verify(manifest, version, asset, hash, key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        Assert.Equal(expected, verified);
    }

    [Fact]
    public void Verify_ChangedPayloadOrUntrustedKey_RejectsUpdate()
    {
        using Key trusted = Key.Create(SignatureAlgorithm.Ed25519);
        using Key attacker = Key.Create(SignatureAlgorithm.Ed25519);
        string hash = new('a', 64);
        byte[] signature = SignatureAlgorithm.Ed25519.Sign(attacker, UpdateSignature.GetMessage("1.1.0", "Kadr-win-x64.exe", hash));
        UpdateSignatureManifest manifest = new("1.1.0", "Kadr-win-x64.exe", hash, Convert.ToBase64String(signature));
        byte[] publicKey = trusted.PublicKey.Export(KeyBlobFormat.RawPublicKey);

        Assert.False(UpdateSignature.Verify(manifest, "1.1.0", "Kadr-win-x64.exe", hash, publicKey));
        Assert.False(UpdateSignature.Verify(manifest, "1.1.0", "Kadr-win-x64.exe", new string('b', 64), publicKey));
        Assert.False(UpdateSignature.Verify(manifest with { Signature = "invalid" }, "1.1.0", "Kadr-win-x64.exe", hash, publicKey));
    }
}
