using Microsoft.Extensions.Options;
using Shared.Crypto;
using Shared.DTOs;
using System.Security.Cryptography;

namespace IngestionService.Services;

/// <summary>
/// Thin wrapper around Shared.Crypto.CryptoHelper.
/// All actual crypto logic lives in CryptoHelper so it's shared with SensorClient.
///
/// Uses ECDSA P-256 + SHA256 for signature verification (matches SensorClient).
/// Uses AES-256-CBC for payload decryption (matches SensorClient).
/// </summary>
public class CryptoService
{
    private readonly CryptoOptions _opts;
    private readonly ILogger<CryptoService> _logger;

    // Cache loaded ECDsa objects so we don't re-parse Base64 on every request
    private readonly Dictionary<string, ECDsa> _publicKeys;

    public CryptoService(IOptions<CryptoOptions> opts, ILogger<CryptoService> logger)
    {
        _opts = opts.Value;
        _logger = logger;

        // Pre-load all public keys from config at startup
        _publicKeys = new Dictionary<string, ECDsa>();
        foreach (var (sensorId, base64Key) in _opts.SensorKeys)
        {
            try
            {
                _publicKeys[sensorId] = CryptoHelper.LoadPublicKey(base64Key);
                _logger.LogInformation("Loaded public key for {SensorId}", sensorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load public key for {SensorId}", sensorId);
            }
        }
    }

    // -------------------------------------------------------------------------
    // AES-256-CBC decryption
    // Delegates entirely to CryptoHelper.DecryptAes — same implementation
    // as encryption on the SensorClient side.
    // -------------------------------------------------------------------------
    public string DecryptPayload(string encryptedPayloadBase64)
    {
        var aesKey = Convert.FromBase64String(_opts.AesKey);
        return CryptoHelper.DecryptAes(encryptedPayloadBase64, aesKey);
    }

    // -------------------------------------------------------------------------
    // ECDSA P-256 + SHA256 signature verification
    // Uses CryptoHelper.BuildSigningInput to build the canonical string —
    // MUST match exactly what SensorClient signs.
    // -------------------------------------------------------------------------
    public bool VerifySignature(EncryptedMessageDto message)
    {
        if (!_publicKeys.TryGetValue(message.SensorId, out var publicKey))
        {
            _logger.LogWarning("No public key registered for sensor {SensorId}", message.SensorId);
            return false;
        }

        // Canonical string — identical to what SensorClient passes to CryptoHelper.Sign()
        var signingInput = CryptoHelper.BuildSigningInput(
            message.SensorId,
            message.MessageId,
            message.SentAt,
            message.EncryptedPayload);

        try
        {
            return CryptoHelper.Verify(signingInput, message.Signature, publicKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Signature verification threw for sensor {SensorId}", message.SensorId);
            return false;
        }
    }
}

// ---------------------------------------------------------------------------
// Bound to "Crypto" section in appsettings.json
// Matches SensorClient config structure:
//   Crypto:AesKey          → shared AES-256 key (Base64)
//   Crypto:SensorKeys      → sensorId → ECDSA public key (Base64 SubjectPublicKeyInfo)
// ---------------------------------------------------------------------------
public class CryptoOptions
{
    public const string Section = "Crypto";

    /// <summary>Base64-encoded 32-byte AES-256 key, shared with SensorClient.</summary>
    public string AesKey { get; set; } = string.Empty;

    /// <summary>sensorId → Base64 SubjectPublicKeyInfo (ECDSA P-256 public key).</summary>
    public Dictionary<string, string> SensorKeys { get; set; } = new();
}