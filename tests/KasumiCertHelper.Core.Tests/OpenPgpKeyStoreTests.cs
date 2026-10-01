using System.Text;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>Covers the key store that replaces a GnuPG keyring.</summary>
public class OpenPgpKeyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "kasumi-pgp-store-tests",
        Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception)
        {
        }
    }

    private OpenPgpKeyStore CreateStore() => new(_directory);

    private static OpenPgpKeyPair Generate(string email, OpenPgpKeyAlgorithm algorithm = OpenPgpKeyAlgorithm.Ed25519) =>
        OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Store Test",
            Email = email,
            Algorithm = algorithm,
            KeySize = 2048,
            Passphrase = "pw-123456",
            ValidDays = 365,
        });

    [Fact]
    public void StoresAndListsAGeneratedKey()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpKeyPair pair = Generate("list@example.com");
        OpenPgpStoredKey stored = store.Add(pair, "generated in a test");

        Assert.Equal(pair.Fingerprint, stored.Fingerprint);
        Assert.True(stored.HasSecretKey);
        Assert.True(stored.IsSecretProtected);
        Assert.False(stored.IsExpired);
        Assert.Equal("generated in a test", stored.Note);
        Assert.True(stored.CanEncrypt, "生成的密钥应当带有加密子密钥。");
        Assert.True(stored.CanSign);

        OpenPgpStoredKey listed = Assert.Single(store.List());
        Assert.Equal(pair.Fingerprint, listed.Fingerprint);
        Assert.Equal("Store Test <list@example.com>", listed.UserId);
        Assert.Equal("EdDSA", listed.Algorithm);
    }

    /// <summary>A key is editable by copying files in, which is how GnuPG interoperability works.</summary>
    [Fact]
    public void ImportsPublicAndSecretKeysFromArmor()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpKeyPair publicOnly = Generate("public@example.com");
        OpenPgpKeyPair withSecret = Generate("secret@example.com");

        OpenPgpImportResult publicResult = store.Import(publicOnly.PublicKeyArmor);
        Assert.Single(publicResult.Imported);
        Assert.False(store.Find(publicOnly.Fingerprint)!.HasSecretKey);

        OpenPgpImportResult secretResult = store.Import(withSecret.SecretKeyArmor);
        Assert.Single(secretResult.Imported);
        Assert.True(store.Find(withSecret.Fingerprint)!.HasSecretKey);

        // Importing the same secret key again must not report a second, different key.
        OpenPgpImportResult again = store.Import(withSecret.SecretKeyArmor);
        Assert.Empty(again.Imported);
        Assert.Single(again.Updated);

        Assert.Equal(2, store.List().Count);
    }

    [Fact]
    public void ImportedSecretKeyStillEncryptsThroughItsSubkey()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpKeyPair pair = Generate("roundtrip@example.com");
        store.Import(pair.SecretKeyArmor);

        OpenPgpStoredKey stored = store.Find(pair.Fingerprint)!;
        byte[] ciphertext = OpenPgp.Encrypt(
            Encoding.UTF8.GetBytes("stored key round trip"),
            new[] { stored.PublicKeyArmor },
            armor: true);

        byte[] plaintext = OpenPgp.Decrypt(
            ciphertext,
            store.GetSecretArmor(stored.Fingerprint)!,
            "pw-123456");

        Assert.Equal("stored key round trip", Encoding.UTF8.GetString(plaintext));
    }

    [Fact]
    public void DeletingRemovesEveryFileOfTheKey()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpKeyPair pair = Generate("delete@example.com");
        store.Add(pair, "note");

        Assert.True(store.Delete(pair.Fingerprint));
        Assert.Empty(store.List());
        Assert.Null(store.GetSecretArmor(pair.Fingerprint));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    [Fact]
    public void UnreadableFilesAreSkippedInsteadOfBreakingTheList()
    {
        OpenPgpKeyStore store = CreateStore();
        store.Add(Generate("good@example.com"));

        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "BROKEN.pub.asc"), "not an OpenPGP key");

        OpenPgpStoredKey listed = Assert.Single(store.List());
        Assert.Contains("good@example.com", listed.UserId);
    }

    [Fact]
    public void ImportOfSomethingElseReportsThatNothingWasAdded()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpImportResult result = store.Import("hello, this is not a key");

        Assert.Empty(result.Imported);
        Assert.NotEmpty(result.Skipped);
        Assert.Empty(store.List());
    }

    [Fact]
    public void FindsKeysByFingerprintIgnoringSeparators()
    {
        OpenPgpKeyStore store = CreateStore();
        OpenPgpKeyPair pair = Generate("find@example.com");
        store.Add(pair);

        string spaced = pair.Fingerprint.Insert(4, " ");
        Assert.NotNull(store.Find(spaced));
        Assert.NotNull(store.Find(pair.Fingerprint.ToLowerInvariant()));
    }
}
