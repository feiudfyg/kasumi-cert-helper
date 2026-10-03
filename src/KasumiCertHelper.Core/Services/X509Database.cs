using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using KasumiCertHelper.Core.Localization;
using System.Text.Json.Serialization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public sealed class X509DatabasePayload
{
    public int Version { get; set; } = 1;

    public string? Description { get; set; }

    /// <summary>
    /// A salted hash of the database password, so an empty database can still tell passwords apart.
    /// Older files have none; they are validated against the first private key instead.
    /// </summary>
    public string? PasswordCheck { get; set; }

    public List<X509Item> Items { get; set; } = new();
}

/// <summary>The password does not open the database.</summary>
public sealed class WrongPasswordException : Exception
{
    public WrongPasswordException()
        : base(Loc.Get("X509_ErrorWrongPassword"))
    {
    }
}

public sealed class X509Database
{
    private const int PasswordCheckIterations = 100_000;
    private const int PasswordCheckSaltBytes = 16;
    private const int PasswordCheckHashBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string _password;
    private string? _passwordCheck;

    private X509Database(string filePath, string password, string? passwordCheck)
    {
        FilePath = filePath;
        _password = password;
        _passwordCheck = passwordCheck;
    }

    public string FilePath { get; }

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public ObservableCollection<X509Item> Items { get; private set; } = new();

    /// <summary>Only for the launcher and the settings page; never logged or written anywhere.</summary>
    internal string Password => _password;

    public static X509Database Create(string filePath, string password)
    {
        var database = new X509Database(filePath, password, CreatePasswordCheck(password));
        database.Save();
        return database;
    }

    public static X509Database Open(string filePath, string password, bool validatePassword = true)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(Loc.Get("Error_DatabaseFileMissing"), filePath);
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        X509DatabasePayload payload = JsonSerializer.Deserialize<X509DatabasePayload>(json, JsonOptions)
                                      ?? new X509DatabasePayload();
        var database = new X509Database(filePath, password, payload.PasswordCheck)
        {
            Items = new ObservableCollection<X509Item>(payload.Items),
        };

        if (validatePassword && !database.ValidatePassword(password))
        {
            throw new WrongPasswordException();
        }

        // A file written before the check existed: the password was just proven by decrypting a key,
        // so it is safe to remember it. An empty legacy file proves nothing, so it keeps the old
        // lenient behaviour of accepting any password.
        if (database._passwordCheck is null && database.Items.Any(i => i.HasPrivateKey))
        {
            database._passwordCheck = CreatePasswordCheck(password);
            database.Save();
        }

        return database;
    }


    public void Save()
    {
        var payload = new X509DatabasePayload
        {
            Version = 1,
            PasswordCheck = _passwordCheck,
            Items = Items.ToList(),
        };
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(FilePath, json, new UTF8Encoding(false));
    }

    public X509Item AddKey(string name, AsymmetricAlgorithm key, string comment = "")
    {
        var item = new X509Item
        {
            Kind = X509ItemKind.PrivateKey,
            Name = name,
            Comment = comment,
            EncryptedKeyPem = key.ExportEncryptedPkcs8PrivateKeyPem(_password, CertificateKeyIO.DefaultPbe),
            KeyAlgorithm = X509Factory.GetKeyAlgorithmName(key),
            KeySize = key.KeySize,
        };
        Items.Add(item);
        Save();
        return item;
    }

    public X509Item AddCertificate(string name, X509Certificate2 certificate, string? keyId, string comment = "")
    {
        var item = new X509Item
        {
            Kind = X509ItemKind.Certificate,
            Name = name,
            Comment = comment,
            KeyId = keyId,
            CertificatePem = certificate.ExportCertificatePem(),
            Subject = certificate.Subject,
            Issuer = certificate.Issuer,
            Serial = certificate.SerialNumber,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            IsCa = CertificateDetailsBuilder.IsCa(certificate),
            SignatureAlgorithm = certificate.SignatureAlgorithm?.FriendlyName,
        };
        (item.KeyAlgorithm, item.KeySize) = GetKeyInfo(certificate);
        Items.Add(item);
        Save();
        return item;
    }

    public X509Item AddCsr(string name, CertificateRequest request, string? keyId, string comment = "")
    {
        var item = new X509Item
        {
            Kind = X509ItemKind.Csr,
            Name = name,
            Comment = comment,
            KeyId = keyId,
            CsrPem = request.CreateSigningRequestPem(),
            Subject = request.SubjectName.Name,
        };
        Items.Add(item);
        Save();
        return item;
    }

    public X509Item ImportCertificate(string name, string certificatePem, string? keyPem, string comment = "")
    {
        X509Certificate2 certificate = X509Certificate2.CreateFromPem(certificatePem);
        string? keyId = null;
        if (!string.IsNullOrWhiteSpace(keyPem))
        {
            X509Item keyItem = ImportKey(name + Loc.Get("X509_KeyItemSuffix"), keyPem, null, comment);
            keyId = keyItem.Id;
        }
        return AddCertificate(name, certificate, keyId, comment);
    }

    public X509Item ImportKey(string name, string keyPem, string? password, string comment = "")
    {
        using AsymmetricAlgorithm key = X509Factory.LoadPrivateKeyFromPem(keyPem, password);
        return AddKey(name, key, comment);
    }

    public X509Item ImportCsr(string name, string csrPem, string comment = "")
    {
        CertificateRequest request = X509Factory.LoadCsr(csrPem);
        return AddCsr(name, request, null, comment);
    }

    public void Remove(X509Item item)
    {
        if (item.Kind == X509ItemKind.PrivateKey)
        {
            foreach (X509Item dependent in Items.Where(i => i.KeyId == item.Id).ToList())
            {
                dependent.KeyId = null;
            }
        }
        Items.Remove(item);
        Save();
    }

    public void Rename(X509Item item, string name, string comment)
    {
        item.Name = name;
        item.Comment = comment;
        Save();
    }

    public X509Item? FindKey(X509Item item)
        => item.KeyId is null ? null : Items.FirstOrDefault(i => i.Id == item.KeyId);

    public AsymmetricAlgorithm LoadKey(X509Item item, string password)
    {
        if (string.IsNullOrEmpty(item.EncryptedKeyPem))
        {
            throw new CryptographicException(Loc.Get("Error_ItemHasNoPrivateKey"));
        }
        return X509Factory.LoadPrivateKeyFromPem(item.EncryptedKeyPem, password);
    }

    public AsymmetricAlgorithm LoadKey(X509Item item) => LoadKey(item, _password);

    public string ExportPrivateKeyPem(X509Item item, string? password)
    {
        using AsymmetricAlgorithm key = LoadKey(item);
        return string.IsNullOrEmpty(password)
            ? key.ExportPkcs8PrivateKeyPem()
            : key.ExportEncryptedPkcs8PrivateKeyPem(password, CertificateKeyIO.DefaultPbe);
    }

    public X509Certificate2? GetCertificate(X509Item item)
    {
        if (string.IsNullOrEmpty(item.CertificatePem))
        {
            return null;
        }
        return X509Certificate2.CreateFromPem(item.CertificatePem);
    }

    public X509Certificate2? GetCertificateWithKey(X509Item item)
    {
        X509Certificate2? certificate = GetCertificate(item);
        if (certificate is null)
        {
            return null;
        }

        X509Item? keyItem = FindKey(item);
        if (keyItem is null)
        {
            return certificate;
        }

        using AsymmetricAlgorithm key = LoadKey(keyItem);
        return CertificateKeyIO.CopyWithPrivateKey(certificate, key);
    }

    public CertificateRequest? GetCsrRequest(X509Item item)
        => string.IsNullOrEmpty(item.CsrPem) ? null : X509Factory.LoadCsr(item.CsrPem);

    public bool ValidatePassword(string password)
    {
        if (_passwordCheck is not null)
        {
            return PasswordCheckMatches(password, _passwordCheck);
        }

        // A file from before the password check was stored: the only way to test the password is to
        // decrypt a private key. With no private key there is nothing to test, so anything is accepted.
        X509Item? keyItem = Items.FirstOrDefault(i => i.HasPrivateKey);
        if (keyItem is null)
        {
            return true;
        }

        try
        {
            using AsymmetricAlgorithm key = LoadKey(keyItem, password);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void ChangePassword(string oldPassword, string newPassword)
    {
        foreach (X509Item item in Items.Where(i => i.HasPrivateKey).ToList())
        {
            using AsymmetricAlgorithm key = LoadKey(item, oldPassword);
            item.EncryptedKeyPem = key.ExportEncryptedPkcs8PrivateKeyPem(newPassword, CertificateKeyIO.DefaultPbe);
        }
        _password = newPassword;
        _passwordCheck = CreatePasswordCheck(newPassword);
        Save();
    }

    private static string CreatePasswordCheck(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(PasswordCheckSaltBytes);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, PasswordCheckIterations, HashAlgorithmName.SHA256, PasswordCheckHashBytes);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private static bool PasswordCheckMatches(string password, string check)
    {
        string[] parts = check.Split(':');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, PasswordCheckIterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static (string? Algorithm, int? Size) GetKeyInfo(X509Certificate2 certificate)
    {
        try
        {
            using AsymmetricAlgorithm? key = CertificateKeyIO.GetPublicKey(certificate);
            if (key is null)
            {
                return (null, null);
            }
            return (X509Factory.GetKeyAlgorithmName(key), key.KeySize);
        }
        catch
        {
            return (null, null);
        }
    }
}
