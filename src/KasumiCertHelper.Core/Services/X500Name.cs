using System.Text;

namespace KasumiCertHelper.Core.Services;

public sealed record X500NamePart(string Type, string Oid, string Value);

public static class X500Name
{
    private static readonly Dictionary<string, string> NameToOid = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CN"] = "2.5.4.3",
        ["COMMONNAME"] = "2.5.4.3",
        ["SN"] = "2.5.4.4",
        ["SURNAME"] = "2.5.4.4",
        ["SERIALNUMBER"] = "2.5.4.5",
        ["C"] = "2.5.4.6",
        ["COUNTRY"] = "2.5.4.6",
        ["COUNTRYNAME"] = "2.5.4.6",
        ["L"] = "2.5.4.7",
        ["LOCALITY"] = "2.5.4.7",
        ["LOCALITYNAME"] = "2.5.4.7",
        ["ST"] = "2.5.4.8",
        ["S"] = "2.5.4.8",
        ["STATE"] = "2.5.4.8",
        ["STATEORPROVINCENAME"] = "2.5.4.8",
        ["STREET"] = "2.5.4.9",
        ["STREETADDRESS"] = "2.5.4.9",
        ["O"] = "2.5.4.10",
        ["ORGANIZATION"] = "2.5.4.10",
        ["ORGANIZATIONNAME"] = "2.5.4.10",
        ["OU"] = "2.5.4.11",
        ["ORGANIZATIONALUNIT"] = "2.5.4.11",
        ["ORGANIZATIONALUNITNAME"] = "2.5.4.11",
        ["T"] = "2.5.4.12",
        ["TITLE"] = "2.5.4.12",
        ["GIVENNAME"] = "2.5.4.42",
        ["GN"] = "2.5.4.42",
        ["INITIALS"] = "2.5.4.43",
        ["GENERATIONQUALIFIER"] = "2.5.4.44",
        ["DNQUALIFIER"] = "2.5.4.46",
        ["PSEUDONYM"] = "2.5.4.65",
        ["DOMAINCOMPONENT"] = "0.9.2342.19200300.100.1.25",
        ["DC"] = "0.9.2342.19200300.100.1.25",
        ["USERID"] = "0.9.2342.19200300.100.1.1",
        ["UID"] = "0.9.2342.19200300.100.1.1",
        ["EMAIL"] = "1.2.840.113549.1.9.1",
        ["E"] = "1.2.840.113549.1.9.1",
        ["EMAILADDRESS"] = "1.2.840.113549.1.9.1",
        ["POSTALCODE"] = "2.5.4.17",
        ["POSTOFFICEBOX"] = "2.5.4.18",
        ["PHONENUMBER"] = "2.5.4.20",
        ["BUSINESSCATEGORY"] = "2.5.4.15",
    };

    private static readonly Dictionary<string, string> OidToShortName = BuildOidMap();

    private static Dictionary<string, string> BuildOidMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in NameToOid)
        {
            if (pair.Key.Length > 2 && !map.ContainsKey(pair.Value))
            {
                map[pair.Value] = pair.Key;
            }
        }
        map["2.5.4.3"] = "CN";
        map["2.5.4.10"] = "O";
        map["2.5.4.11"] = "OU";
        map["2.5.4.6"] = "C";
        map["2.5.4.7"] = "L";
        map["2.5.4.8"] = "ST";
        map["1.2.840.113549.1.9.1"] = "E";
        map["0.9.2342.19200300.100.1.25"] = "DC";
        return map;
    }

    public static string? ToOidOrNull(string token)
        => NameToOid.TryGetValue(token.Trim(), out string? oid) ? oid : null;

    public static string ToShortName(string oid)
        => OidToShortName.TryGetValue(oid.Trim(), out string? name) ? name : oid.Trim();

    public static List<X500NamePart> Parse(string? distinguishedName)
    {
        var parts = new List<X500NamePart>();
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return parts;
        }

        foreach (string token in SplitDn(distinguishedName))
        {
            int eq = IndexOfUnescaped(token, '=');
            if (eq <= 0)
            {
                continue;
            }

            string type = token[..eq].Trim();
            string value = UnescapeValue(token[(eq + 1)..].Trim());
            string oid = ToOidOrNull(type) ?? type;
            parts.Add(new X500NamePart(ToShortName(type), oid, value));
        }

        return parts;
    }

    public static string GetCommonName(string? distinguishedName)
    {
        foreach (X500NamePart part in Parse(distinguishedName))
        {
            if (part.Oid == "2.5.4.3")
            {
                return part.Value;
            }
        }
        return (distinguishedName ?? string.Empty).Trim();
    }

    public static string? GetFirstValue(string? distinguishedName, string attributeName)
    {
        string oid = ToOidOrNull(attributeName) ?? attributeName;
        foreach (X500NamePart part in Parse(distinguishedName))
        {
            if (string.Equals(part.Oid, oid, StringComparison.OrdinalIgnoreCase))
            {
                return part.Value;
            }
        }
        return null;
    }

    public static string Format(string? distinguishedName)
    {
        List<X500NamePart> parts = Parse(distinguishedName);
        if (parts.Count == 0)
        {
            return (distinguishedName ?? string.Empty).Trim();
        }
        return string.Join(", ", parts.Select(p => $"{p.Type}={p.Value}"));
    }

    public static bool IsValid(string? distinguishedName)
    {
        if (string.IsNullOrWhiteSpace(distinguishedName))
        {
            return false;
        }
        try
        {
            _ = new System.Security.Cryptography.X509Certificates.X500DistinguishedName(distinguishedName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SplitDn(string dn)
    {
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < dn.Length; i++)
        {
            char c = dn[i];
            if (c == '\\' && i + 1 < dn.Length)
            {
                current.Append(c);
                current.Append(dn[i + 1]);
                i++;
                continue;
            }
            if (c == '"')
            {
                inQuotes = !inQuotes;
                current.Append(c);
                continue;
            }
            if (!inQuotes && (c == ',' || c == ';' || c == '+'))
            {
                yield return current.ToString();
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static int IndexOfUnescaped(string token, char target)
    {
        for (int i = 0; i < token.Length; i++)
        {
            if (token[i] == '\\')
            {
                i++;
                continue;
            }
            if (token[i] == target)
            {
                return i;
            }
        }
        return -1;
    }

    private static string UnescapeValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        if (!value.Contains('\\'))
        {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                if (i + 1 < value.Length && Uri.IsHexDigit(value[i]) && Uri.IsHexDigit(value[i + 1]))
                {
                    sb.Append((char)Convert.ToByte(value.Substring(i, 2), 16));
                    i++;
                }
                else
                {
                    sb.Append(value[i]);
                }
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }
}
