namespace Kadr.Desktop.Services.Updates;

public sealed record UpdateSignatureManifest(string Version, string AssetName, string Sha256, string Signature);
