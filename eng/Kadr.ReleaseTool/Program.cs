using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

using NSec.Cryptography;
using Kadr.Desktop.Services.Updates;

namespace Kadr.ReleaseTool;

internal static class Program
{
    private static readonly string PrivateKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kadr", "ReleaseSigning", "private-key.bin");

    internal static async Task<int> Main(string[] args)
    {
        try
        {
            if (args is ["keygen", string publicPath])
            {
                GenerateKey(publicPath);
            }
            else if (args is ["sign", string file, string version])
            {
                await SignAsync(file, Version.Parse(version).ToString(), CancellationToken.None).ConfigureAwait(false);
            }
            else if (args is ["export-key", string exportPath])
            {
                byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(PrivateKeyPath), null, DataProtectionScope.CurrentUser);
                try
                {
                    using FileStream output = new(exportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    byte[] encoded = System.Text.Encoding.ASCII.GetBytes(Convert.ToBase64String(plain));
                    try
                    {
                        output.Write(encoded);
                        output.Flush(true);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(encoded);
                    }
                    Console.WriteLine("Exported private key. Keep this file outside Git and back it up securely: " + Path.GetFullPath(exportPath));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            else if (args is ["import-key", string importPath])
            {
                if (File.Exists(PrivateKeyPath))
                {
                    throw new IOException("A protected signing key already exists.");
                }
                byte[] plain = Convert.FromBase64String(File.ReadAllText(importPath));
                try
                {
                    using Key imported = Key.Import(SignatureAlgorithm.Ed25519, plain, KeyBlobFormat.RawPrivateKey);
                    VerifyPublicKey(imported);
                    Directory.CreateDirectory(Path.GetDirectoryName(PrivateKeyPath)!);
                    using FileStream output = new(PrivateKeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    output.Write(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
                    output.Flush(true);
                    Console.WriteLine("Imported protected signing key: " + PrivateKeyPath);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plain);
                }
            }
            else
            {
                Console.Error.WriteLine("Usage: keygen <public-key.txt> | sign <Kadr-win-x64.exe> <version> | export-key <backup.key> | import-key <backup.key>");
                return 1;
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void GenerateKey(string publicPath)
    {
        if (File.Exists(PrivateKeyPath) || File.Exists(publicPath))
        {
            throw new IOException("A signing key already exists. Key rotation requires a separately planned release.");
        }
        using Key key = Key.Create(SignatureAlgorithm.Ed25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        byte[] plain = key.Export(KeyBlobFormat.RawPrivateKey);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrivateKeyPath)!);
            using FileStream privateFile = new(PrivateKeyPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            privateFile.Write(ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
            privateFile.Flush(true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(publicPath))!);
            File.WriteAllText(publicPath, Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey)) + "\n");
            Console.WriteLine("Created protected signing key: " + PrivateKeyPath);
            Console.WriteLine("Created public verification key: " + Path.GetFullPath(publicPath));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static async Task SignAsync(string file, string version, CancellationToken ct)
    {
        if (Path.GetFileName(file) != "Kadr-win-x64.exe")
        {
            throw new ArgumentException("The release asset must be named Kadr-win-x64.exe.");
        }
        string productVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(file).ProductVersion?.Split('+')[0]
            ?? throw new InvalidDataException("The executable has no product version.");
        if (Version.Parse(productVersion) != Version.Parse(version))
        {
            throw new InvalidDataException("The release version does not match the executable's product version.");
        }
        byte[] encrypted = await File.ReadAllBytesAsync(PrivateKeyPath, ct).ConfigureAwait(false);
        byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            using Key key = Key.Import(SignatureAlgorithm.Ed25519, plain, KeyBlobFormat.RawPrivateKey);
            VerifyPublicKey(key);
            await using FileStream input = File.OpenRead(file);
            string hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, ct).ConfigureAwait(false));
            string name = Path.GetFileName(file);
            byte[] signature = SignatureAlgorithm.Ed25519.Sign(key, UpdateSignature.GetMessage(version, name, hash));
            UpdateSignatureManifest manifest = new(version, name, hash, Convert.ToBase64String(signature));
            await File.WriteAllTextAsync(file + ".sig", JsonSerializer.Serialize(manifest), ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(file + ".sha256", hash + "  " + name + "\n", ct).ConfigureAwait(false);
            Console.WriteLine("Signed " + name + " " + version);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static void VerifyPublicKey(Key key)
    {
        using Stream publicStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kadr.UpdatePublicKey")
            ?? throw new InvalidOperationException("The public key is missing. Generate a key pair first.");
        using StreamReader reader = new(publicStream);
        byte[] publicKey = Convert.FromBase64String(reader.ReadToEnd());
        if (!CryptographicOperations.FixedTimeEquals(publicKey, key.PublicKey.Export(KeyBlobFormat.RawPublicKey)))
        {
            throw new CryptographicException("The signing key does not match this application's verification key.");
        }
    }
}
