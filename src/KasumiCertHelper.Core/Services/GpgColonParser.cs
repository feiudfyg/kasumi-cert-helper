using System.Text;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public static class GpgColonParser
{
    private const int FieldValidity = 2;
    private const int FieldLength = 3;
    private const int FieldAlgorithm = 4;
    private const int FieldKeyId = 5;
    private const int FieldCreated = 6;
    private const int FieldExpires = 7;
    private const int FieldUserId = 10;
    private const int FieldFingerprint = 10;
    private const int FieldCapabilities = 12;
    private const int FieldCurve = 17;

    public static IReadOnlyList<GpgKey> Parse(string colonOutput)
    {
        var keys = new List<GpgKey>();
        if (string.IsNullOrWhiteSpace(colonOutput))
        {
            return keys;
        }

        GpgKey? currentKey = null;
        GpgSubkey? currentSubkey = null;
        bool keyFingerprintAssigned = false;

        foreach (string rawLine in colonOutput.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(':');
            if (fields.Length == 0)
            {
                continue;
            }

            switch (fields[0])
            {
                case "pub":
                case "sec":
                {
                    currentKey = new GpgKey
                    {
                        KeyId = Field(fields, FieldKeyId),
                        Algorithm = MapAlgorithm(Field(fields, FieldAlgorithm)),
                        Curve = Field(fields, FieldCurve),
                        Length = ParseInt(Field(fields, FieldLength)),
                        Created = FromUnix(Field(fields, FieldCreated)),
                        Expires = FromUnix(Field(fields, FieldExpires)),
                        Capabilities = Field(fields, FieldCapabilities),
                        Validity = Field(fields, FieldValidity),
                        HasSecret = fields[0] == "sec",
                    };
                    currentSubkey = null;
                    keyFingerprintAssigned = false;
                    keys.Add(currentKey);
                    break;
                }

                case "sub":
                case "ssb":
                {
                    if (currentKey is null)
                    {
                        break;
                    }

                    currentSubkey = new GpgSubkey
                    {
                        KeyId = Field(fields, FieldKeyId),
                        Algorithm = MapAlgorithm(Field(fields, FieldAlgorithm)),
                        Curve = Field(fields, FieldCurve),
                        Length = ParseInt(Field(fields, FieldLength)),
                        Created = FromUnix(Field(fields, FieldCreated)),
                        Expires = FromUnix(Field(fields, FieldExpires)),
                        Capabilities = Field(fields, FieldCapabilities),
                        HasSecret = fields[0] == "ssb",
                    };
                    currentKey.Subkeys.Add(currentSubkey);
                    break;
                }

                case "fpr":
                {
                    string fingerprint = Field(fields, FieldFingerprint);
                    if (fingerprint.Length == 0)
                    {
                        break;
                    }

                    if (currentSubkey is not null && currentSubkey.Fingerprint.Length == 0)
                    {
                        currentSubkey.Fingerprint = fingerprint;
                    }
                    else if (currentKey is not null && !keyFingerprintAssigned)
                    {
                        currentKey.Fingerprint = fingerprint;
                        keyFingerprintAssigned = true;
                    }
                    break;
                }

                case "uid":
                {
                    if (currentKey is null)
                    {
                        break;
                    }

                    string value = Unescape(Field(fields, FieldUserId));
                    if (value.Length > 0)
                    {
                        currentKey.UserIds.Add(new GpgUid
                        {
                            Value = value,
                            Validity = Field(fields, FieldValidity),
                            IsPrimary = currentKey.UserIds.Count == 0,
                        });
                    }
                    break;
                }

                case "rvk":
                {
                    if (currentKey is not null)
                    {
                        currentKey.IsRevoked = true;
                    }
                    break;
                }
            }
        }

        foreach (GpgKey key in keys)
        {
            if (key.Fingerprint.Length == 0)
            {
                key.Fingerprint = key.KeyId;
            }

            key.IsRevoked |= key.Validity == "r" || HasCapability(key.Capabilities, 'r');
            key.IsDisabled |= key.Validity == "d" || HasCapability(key.Capabilities, 'd');
            key.IsExpired |= key.Validity == "e" || (key.Expires is not null && key.Expires.Value < DateTime.Now);
        }

        return keys;
    }

    private static bool HasCapability(string capabilities, char capability)
        => capabilities.Length > 0 && capabilities.Contains(capability);

    private static string Field(string[] fields, int oneBasedIndex)
        => oneBasedIndex >= 1 && oneBasedIndex <= fields.Length ? fields[oneBasedIndex - 1] : string.Empty;

    private static int ParseInt(string value) => int.TryParse(value, out int result) ? result : 0;

    private static DateTime? FromUnix(string value)
        => long.TryParse(value, out long seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime
            : null;

    private static string MapAlgorithm(string code) => code switch
    {
        "1" or "2" or "3" => "RSA",
        "16" or "20" => "Elgamal",
        "17" => "DSA",
        "18" or "23" => "ECDH",
        "19" => "ECDSA",
        "21" => "Diffie-Hellman",
        "22" => "EdDSA",
        "24" or "27" => "Ed25519",
        "25" => "X25519",
        "26" or "28" => "X448",
        _ => code.Length == 0 ? Loc.Get("Gpg_Validity_Unknown") : Loc.Format("Gpg_AlgorithmPrefix", code),
    };

    private static string Unescape(string value)
    {
        if (value.IndexOf("\\x", StringComparison.Ordinal) < 0)
        {
            return value;
        }

        var bytes = new List<byte>(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length && value[i + 1] == 'x'
                && Uri.IsHexDigit(value[i + 2]) && Uri.IsHexDigit(value[i + 3]))
            {
                bytes.Add(Convert.ToByte(value.Substring(i + 2, 2), 16));
                i += 3;
            }
            else
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(value[i].ToString()));
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
