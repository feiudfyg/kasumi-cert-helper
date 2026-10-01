using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Core.Models;

/// <summary>
/// A key held by the application's own OpenPGP key store: the public half always, the secret half
/// when this installation generated or imported it.
/// </summary>
public sealed record OpenPgpStoredKey(
    string Fingerprint,
    string KeyId,
    string UserId,
    string Algorithm,
    int KeySize,
    DateTime Created,
    DateTime? Expires,
    bool HasSecretKey,
    bool IsSecretProtected,
    bool IsRevoked,
    bool CanEncrypt,
    bool CanSign,
    string? Note,
    string PublicKeyArmor)
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

    public bool IsExpired => Expires is not null && Expires.Value < DateTime.Now;

    public string StatusText => Loc.Get(
        IsRevoked ? "Gpg_State_Revoked"
        : IsExpired ? "Gpg_State_Expired"
        : "Gpg_State_Valid");

    public string SecretText => Loc.Get(HasSecretKey ? "Gpg_HasSecret" : "Gpg_PublicKey");

    public string AlgorithmText => KeySize > 0 ? $"{Algorithm} {KeySize}" : Algorithm;

    public string ExpiresText => Expires?.ToString("yyyy-MM-dd") ?? Loc.Get("Gpg_NeverExpires");

    public string CreatedText => Created.ToString("yyyy-MM-dd");

    public string CapabilitiesText
    {
        get
        {
            var parts = new List<string>();
            if (CanEncrypt)
            {
                parts.Add(Loc.Get("Gpg_Capability_Encrypt"));
            }

            if (CanSign)
            {
                parts.Add(Loc.Get("Gpg_Capability_Sign"));
            }

            return parts.Count == 0 ? Loc.Get("Common_None") : string.Join(Loc.Get("Common_ListSeparator"), parts);
        }
    }

    public override string ToString() => $"{UserId} — {ShortFingerprint}";
}

/// <summary>Outcome of importing one file, which may hold several keys.</summary>
public sealed record OpenPgpImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Updated,
    IReadOnlyList<string> Skipped)
{
    public int ImportedCount => Imported.Count;

    public int UpdatedCount => Updated.Count;

    public int SkippedCount => Skipped.Count;
}
