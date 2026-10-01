namespace KasumiCertHelper.Core.Models;

using KasumiCertHelper.Core.Localization;

public sealed class GpgUid
{
    public string Value { get; set; } = string.Empty;

    public string Validity { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public string ValidityText => Loc.Get(Validity switch
    {
        "u" => "Gpg_Validity_Ultimate",
        "f" => "Gpg_Validity_Full",
        "m" => "Gpg_Validity_Marginal",
        "n" => "Gpg_Validity_Never",
        "r" => "Gpg_Validity_Revoked",
        "e" => "Gpg_Validity_Expired",
        "d" => "Gpg_Validity_Disabled",
        "i" => "Gpg_Validity_Invalid",
        _ => "Gpg_Validity_Unknown",
    });

    public override string ToString() => Value;
}

public sealed class GpgSubkey
{
    public string Fingerprint { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string Algorithm { get; set; } = string.Empty;

    public string Curve { get; set; } = string.Empty;

    public int Length { get; set; }

    public DateTime? Created { get; set; }

    public DateTime? Expires { get; set; }

    public bool HasSecret { get; set; }

    public string Capabilities { get; set; } = string.Empty;

    public bool CanEncrypt => Capabilities.Contains('e') || Capabilities.Contains('E');

    public bool CanSign => Capabilities.Contains('s') || Capabilities.Contains('S');

    public bool IsExpired => Expires is not null && Expires.Value < DateTime.Now;

    public string AlgorithmText => Length > 0
        ? (string.IsNullOrEmpty(Curve) ? $"{Algorithm} {Length}" : $"{Algorithm} {Length} ({Curve})")
        : (string.IsNullOrEmpty(Curve) ? Algorithm : $"{Algorithm} ({Curve})");

    public override string ToString() => $"{Fingerprint} {AlgorithmText}";
}

public sealed class GpgKey
{
    public string Fingerprint { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string Algorithm { get; set; } = string.Empty;

    public string Curve { get; set; } = string.Empty;

    public int Length { get; set; }

    public DateTime? Created { get; set; }

    public DateTime? Expires { get; set; }

    public bool HasSecret { get; set; }

    public bool IsRevoked { get; set; }

    public bool IsDisabled { get; set; }

    public bool IsExpired { get; set; }

    public string Validity { get; set; } = string.Empty;

    public string Capabilities { get; set; } = string.Empty;

    public List<GpgUid> UserIds { get; set; } = new();

    public List<GpgSubkey> Subkeys { get; set; } = new();

    public string PrimaryUserId => UserIds.Count > 0 ? UserIds[0].Value : Fingerprint;

    public string PrimaryUserEmail
    {
        get
        {
            string value = PrimaryUserId;
            int open = value.LastIndexOf('<');
            int close = value.LastIndexOf('>');
            return open >= 0 && close > open ? value.Substring(open + 1, close - open - 1) : string.Empty;
        }
    }

    public string StatusText
    {
        get
        {
            if (IsRevoked) return Loc.Get("Gpg_State_Revoked");
            if (IsExpired) return Loc.Get("Gpg_State_Expired");
            if (IsDisabled) return Loc.Get("Gpg_State_Disabled");
            return Loc.Get("Gpg_State_Valid");
        }
    }

    public bool CanEncrypt =>
        Capabilities.Contains('e') || Capabilities.Contains('E') ||
        Subkeys.Any(s => s.CanEncrypt);

    public bool CanSign =>
        Capabilities.Contains('s') || Capabilities.Contains('S') ||
        Subkeys.Any(s => s.CanSign);

    public string ShortFingerprint =>
        Fingerprint.Length >= 16 ? Fingerprint[^16..] : Fingerprint;

    public string GroupedFingerprint
    {
        get
        {
            if (Fingerprint.Length == 0)
            {
                return string.Empty;
            }

            var groups = new List<string>();
            for (int i = 0; i < Fingerprint.Length; i += 4)
            {
                groups.Add(Fingerprint.Substring(i, Math.Min(4, Fingerprint.Length - i)));
            }
            return string.Join(' ', groups);
        }
    }

    public string AlgorithmText => Length > 0
        ? (string.IsNullOrEmpty(Curve) ? $"{Algorithm} {Length}" : $"{Algorithm} {Length} ({Curve})")
        : Algorithm;

    public string AlgorithmAndLength => AlgorithmText;

    public string SecretText => Loc.Get(HasSecret ? "Gpg_HasSecret" : "Gpg_PublicKey");

    public string CreatedText => Created?.ToString("yyyy-MM-dd") ?? string.Empty;

    public string ExpiresText => Expires?.ToString("yyyy-MM-dd") ?? Loc.Get("Gpg_NeverExpires");

    public override string ToString() => $"{PrimaryUserId} — {ShortFingerprint}";
}
