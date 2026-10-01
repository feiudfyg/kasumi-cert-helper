namespace KasumiCertHelper.Core.Models;

/// <summary>Key algorithms the OpenPGP engine can generate.</summary>
public enum OpenPgpKeyAlgorithm
{
    Ed25519,
    Ecdsa,
    Rsa,
}

/// <summary>Inputs for <c>OpenPgp.GenerateKeyPair</c>.</summary>
public sealed class OpenPgpKeyOptions
{
    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Comment { get; set; }

    public OpenPgpKeyAlgorithm Algorithm { get; set; } = OpenPgpKeyAlgorithm.Ed25519;

    /// <summary>RSA modulus size in bits.</summary>
    public int KeySize { get; set; } = 3072;

    /// <summary>NIST curve name for ECDSA: <c>P-256</c>, <c>P-384</c> or <c>P-521</c>.</summary>
    public string Curve { get; set; } = "P-256";

    /// <summary>Passphrase protecting the secret key. Empty means the key is stored unprotected.</summary>
    public string? Passphrase { get; set; }

    /// <summary>Days until the key expires, or <c>null</c> for a key that never expires.</summary>
    public int? ValidDays { get; set; }

    /// <summary>Whether an encryption subkey is generated next to the primary key.</summary>
    public bool IncludeEncryptionSubkey { get; set; } = true;
}

/// <summary>A freshly generated key pair, both halves ASCII armored.</summary>
public sealed record OpenPgpKeyPair(
    string Fingerprint,
    string KeyId,
    string UserId,
    string Algorithm,
    int KeySize,
    DateTime Created,
    DateTime? Expires,
    bool HasSecretKey,
    string PublicKeyArmor,
    string SecretKeyArmor);

/// <summary>Facts about a public key that the UI shows.</summary>
public sealed record OpenPgpPublicKey(
    string Fingerprint,
    string KeyId,
    string UserId,
    string Algorithm,
    int KeySize,
    DateTime Created,
    DateTime? Expires,
    bool IsRevoked,
    bool CanEncrypt,
    bool CanSign,
    string Armor)
{
    public string ShortFingerprint => Fingerprint.Length >= 16 ? Fingerprint[^16..] : Fingerprint;

    public string GroupedFingerprint
    {
        get
        {
            var groups = new List<string>();
            for (int i = 0; i < Fingerprint.Length; i += 4)
            {
                groups.Add(Fingerprint.Substring(i, Math.Min(4, Fingerprint.Length - i)));
            }

            return string.Join(' ', groups);
        }
    }

    public bool IsExpired => Expires is not null && Expires.Value < DateTime.UtcNow;
}

/// <summary>Facts about a secret key blob.</summary>
public sealed record OpenPgpSecretKey(
    string Fingerprint,
    string KeyId,
    string Algorithm,
    int KeySize,
    bool IsProtected,
    string Armor);

/// <summary>Outcome of verifying a signature.</summary>
public sealed record OpenPgpVerification(
    bool IsValid,
    string SignerUserId,
    string SignerFingerprint,
    DateTime? Created,
    string Summary);
