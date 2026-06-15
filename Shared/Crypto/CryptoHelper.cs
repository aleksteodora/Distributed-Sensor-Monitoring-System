using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shared.Crypto;

public static class CryptoHelper
{
    /// AES-256-CBC enkripcija, rezultat = Base64(IV[16] + ciphertext)
    /// aesKey mora biti 32 bajta (256 bit)
    public static string EncryptAes(string plainText, byte[] aesKey)
    {
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// AES-256-CBC dekriptovanje. Očekuje Base64(IV[16] + ciphertext)
    /// Koristi Student2 u IngestionService
    public static string DecryptAes(string encryptedBase64, byte[] aesKey)
    {
        var fullBytes = Convert.FromBase64String(encryptedBase64);

        var iv = new byte[16];
        var cipherBytes = new byte[fullBytes.Length - 16];
        Buffer.BlockCopy(fullBytes, 0, iv, 0, 16);
        Buffer.BlockCopy(fullBytes, 16, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// String koji se potpisuje/verifikuje MORA biti isti i na klijentu i na serveru
    public static string BuildSigningInput(string sensorId, long messageId, DateTimeOffset sentAt, string encryptedPayload)
        => $"{sensorId}|{messageId}|{sentAt:O}|{encryptedPayload}";

    /// ECDSA P-256 + SHA256 potpis, vraca Base64
    public static string Sign(string data, ECDsa privateKey)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var signature = privateKey.SignData(bytes, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }

    /// Verifikacija potpisa, Koristi Student2 u IngestionService
    public static bool Verify(string data, string signatureBase64, ECDsa publicKey)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var signature = Convert.FromBase64String(signatureBase64);
        return publicKey.VerifyData(bytes, signature, HashAlgorithmName.SHA256);
    }

    /// Generiše AES-256 ključ i ECDSA P-256 kljucne parove za N senzora,
    /// jednom pokrecemo
    public static void GenerateKeyMaterial(string[] sensorIds)
    {
        var aesKey = Aes.Create();
        aesKey.KeySize = 256;
        aesKey.GenerateKey();
        Console.WriteLine("=== AES KEY (deljeni, ide na klijenta i server) ===");
        Console.WriteLine(Convert.ToBase64String(aesKey.Key));
        Console.WriteLine();

        Console.WriteLine("=== ECDSA KEY PAIRS (po senzoru) ===");
        foreach (var id in sensorIds)
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var privateKey = Convert.ToBase64String(ecdsa.ExportECPrivateKey());
            var publicKey = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

            Console.WriteLine($"{id}:");
            Console.WriteLine($"  Private (SAMO klijent): {privateKey}");
            Console.WriteLine($"  Public  (SAMO server) : {publicKey}");
            Console.WriteLine();
        }
    }

    public static ECDsa LoadPrivateKey(string base64PrivateKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(Convert.FromBase64String(base64PrivateKey), out _);
        return ecdsa;
    }

    public static ECDsa LoadPublicKey(string base64PublicKey)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(base64PublicKey), out _);
        return ecdsa;
    }
}