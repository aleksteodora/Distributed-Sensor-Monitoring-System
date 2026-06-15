using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Shared.DTOs;

namespace IngestionService.Services;

/// <summary>
/// Handles two things:
///   1. AES-256-CBC decryption of the encrypted sensor payload.
///   2. RSA-PSS-SHA256 signature verification to confirm the sender is a known sensor.
///
/// Key storage (development / academic setup):
///   - One shared AES-256 key for all sensors → appsettings.json: Crypto:AesKeyBase64
///   - One RSA-2048 public key per sensor     → appsettings.json: Crypto:SensorPublicKeys
///     (dictionary sensorId → PEM string)
/// </summary>
public class CryptoService
{
    private readonly CryptoOptions _opts;
    private readonly ILogger<CryptoService> _logger;

    public CryptoService(IOptions<CryptoOptions> opts, ILogger<CryptoService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // AES-256-CBC decryption
    //
    // Wire format of EncryptedPayload (after Base64 decode):
    //   [ 16-byte random IV ][ PKCS7-padded ciphertext ]
    //
    // Returns the plaintext JSON string (SensorReadingDto).
    // Throws CryptographicException on bad padding / wrong key.
    // -----------------------------------------------------------------------
    public string DecryptPayload(string encryptedPayloadBase64)
    {
        var fullBytes = Convert.FromBase64String(encryptedPayloadBase64);

        if (fullBytes.Length <= 16)
            throw new CryptographicException("Payload is too short to contain an IV.");

        var iv = fullBytes[..16];
        var ciphertext = fullBytes[16..];

        using var aes = Aes.Create();
        aes.Key = Convert.FromBase64String(_opts.AesKeyBase64);
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
        return Encoding.UTF8.GetString(plain);
    }

    // -----------------------------------------------------------------------
    // RSA-PSS-SHA256 signature verification
    //
    // The sensor signs the canonical string (see BuildSignedData) with its
    // RSA private key. We verify using the matching public key stored in
    // appsettings under Crypto:SensorPublicKeys:{sensorId}.
    //
    // Returns false (and logs a warning) instead of throwing, so the
    // controller can return a clean 401.
    // -----------------------------------------------------------------------
    public bool VerifySignature(EncryptedMessageDto message)
    {
        if (!_opts.SensorPublicKeys.TryGetValue(message.SensorId, out var pem))
        {
            _logger.LogWarning("No public key registered for sensor {SensorId}", message.SensorId);
            return false;
        }

        var dataBytes = Encoding.UTF8.GetBytes(BuildSignedData(message));
        var sigBytes = Convert.FromBase64String(message.Signature);

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return rsa.VerifyData(dataBytes, sigBytes,
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Signature crypto error for sensor {SensorId}", message.SensorId);
            return false;
        }
    }

    // -----------------------------------------------------------------------
    // Canonical string that the sensor signs.
    // MUST match exactly what SensorClient computes before signing.
    // Format: "{sensorId}|{messageId}|{sentAt:o}|{encryptedPayload}"
    // -----------------------------------------------------------------------
    public static string BuildSignedData(EncryptedMessageDto msg)
        => $"{msg.SensorId}|{msg.MessageId}|{msg.SentAt:o}|{msg.EncryptedPayload}";
}

// ---------------------------------------------------------------------------
// Bound to the "Crypto" section in appsettings.json
// ---------------------------------------------------------------------------
public class CryptoOptions
{
    public const string Section = "Crypto";

    /// <summary>Base64-encoded 32-byte (256-bit) AES key, shared with all sensors.</summary>
    public string AesKeyBase64 { get; set; } = string.Empty;

    /// <summary>sensorId → RSA-2048 public key in PEM format.</summary>
    public Dictionary<string, string> SensorPublicKeys { get; set; } = new();
}