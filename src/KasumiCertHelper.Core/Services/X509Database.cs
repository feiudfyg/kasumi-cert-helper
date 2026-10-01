using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KasumiCertHelper.Core.Models;

namespace KasumiCertHelper.Core.Services;

public sealed class X509DatabasePayload
{
    public int Version { get; set; } = 1;

    public string? Description { get; set; }

    public List<X509Item> Items { get; set; } = new();
}

public sealed class X509Database
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private string _password;

    private X509Database(string filePath, string password)
    {
        FilePath = filePath;
        _password = password;
    }

    public string FilePath { get; }

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public ObservableCollection<X509Item> Items { get; private set; } = new();

    public string Password => _password;

    public static X509Database Create(string filePath, string password)
    {
        var database = new X509Database(filePath, password);
        database.Save();
        return database;
    }

    public static X509Database Open(string filePath, string password)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("找不到数据库文件。", filePath);
        }

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        X509DatabasePayload payload = JsonSerializer.Deserialize<X509DatabasePayload>(json, JsonOptions)
                                      ?? new X509DatabasePayload();
        var database = new X509Database(filePath, password)
        {
            Items = new ObservableCollection<X509Item>(payload.Items),
        };
        return database;
    }

    public void Save()
    {
        var payload = new X509DatabasePayload { Version = 1, Items = Items.ToList() };
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
            X509Item keyItem = ImportKey(name + " (密钥)", keyPem, null, comment);
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
            throw new CryptographicException("该项目不包含私钥。");
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

    public void ChangePassword(string oldPassword, string newPassword)    {
        foreach (X509Item item in Items.Where(i => i.HasPrivateKey).ToList())
        {
            using AsymmetricAlgorithm key = LoadKey(item, oldPassword);
            item.EncryptedKeyPem = key.ExportEncryptedPkcs8PrivateKeyPem(newPassword, CertificateKeyIO.DefaultPbe);
        }
        _password = newPassword;
        Save();
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
