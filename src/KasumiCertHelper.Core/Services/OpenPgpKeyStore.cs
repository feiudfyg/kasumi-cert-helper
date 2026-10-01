using System.Text;
using KasumiCertHelper.Core.Localization;
using KasumiCertHelper.Core.Models;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace KasumiCertHelper.Core.Services;

/// <summary>
/// The application's OpenPGP key store. It replaces a GnuPG keyring: the engine works in process, so
/// keys have to live somewhere of our own instead of in <c>%APPDATA%\gnupg</c>.
/// <para>
/// Every key is two files in <c>keys</c>: <c>&lt;fingerprint&gt;.pub.asc</c> and, when the installation
/// holds the private half, <c>&lt;fingerprint&gt;.sec.asc</c>, plus an optional
/// <c>&lt;fingerprint&gt;.note.txt</c> holding the user's own comment. ASCII armored files are
/// deliberately used because they are exactly what gpg reads and writes, so keys move between the two
/// by copying files.
/// </para>
/// </summary>
public sealed class OpenPgpKeyStore
{
    private const string PublicSuffix = ".pub.asc";
    private const string SecretSuffix = ".sec.asc";
    private const string NoteSuffix = ".note.txt";

    public OpenPgpKeyStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
    }

    /// <summary>Folder the keys are stored in.</summary>
    public string Directory { get; }

    /// <summary>Every key, newest first.</summary>
    public IReadOnlyList<OpenPgpStoredKey> List()
    {
        var keys = new List<OpenPgpStoredKey>();

        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return keys;
            }

            foreach (string file in System.IO.Directory.EnumerateFiles(Directory, "*" + PublicSuffix))
            {
                string fingerprint = Path.GetFileName(file)[..^PublicSuffix.Length];
                try
                {
                    keys.Add(Describe(fingerprint));
                }
                catch (Exception)
                {
                    // A file that cannot be parsed is skipped rather than breaking the whole list.
                }
            }
        }
        catch (Exception)
        {
            return keys;
        }

        return keys.OrderByDescending(key => key.HasSecretKey).ThenBy(key => key.UserId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public OpenPgpStoredKey? Find(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        string normalized = NormalizeFingerprint(fingerprint);
        foreach (OpenPgpStoredKey key in List())
        {
            if (string.Equals(key.Fingerprint, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Stores a freshly generated key pair.</summary>
    public OpenPgpStoredKey Add(OpenPgpKeyPair pair, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(pair);

        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(PublicPath(pair.Fingerprint), pair.PublicKeyArmor, Encoding.ASCII);
        File.WriteAllText(SecretPath(pair.Fingerprint), pair.SecretKeyArmor, Encoding.ASCII);

        if (!string.IsNullOrWhiteSpace(note))
        {
            File.WriteAllText(NotePath(pair.Fingerprint), note, Encoding.UTF8);
        }

        return Describe(pair.Fingerprint);
    }

    /// <summary>
    /// Imports armored or binary OpenPGP data. Both public and secret key material is accepted, and a
    /// file may hold several keys.
    /// </summary>
    public OpenPgpImportResult Import(string content, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        System.IO.Directory.CreateDirectory(Directory);

        var imported = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Secret keys first: they carry the public half as well, so importing a private key also
        // creates the public file and the public pass below must not treat it as a second key.
        foreach (PgpSecretKeyRing ring in ReadSecretRings(content))
        {
            string fingerprint = OpenPgp.Fingerprint(ring.GetPublicKey());
            handled.Add(fingerprint);
            Store(
                fingerprint,
                OpenPgp.PublicRingArmorFromSecret(ring),
                Armor(output => ring.Encode(output)),
                note,
                imported,
                updated);
        }

        foreach (PgpPublicKeyRing ring in ReadPublicRings(content))
        {
            string fingerprint = OpenPgp.Fingerprint(ring.GetPublicKey());
            if (!handled.Add(fingerprint))
            {
                continue;
            }

            Store(fingerprint, Armor(output => ring.Encode(output)), null, note, imported, updated);
        }

        if (imported.Count == 0 && updated.Count == 0)
        {
            skipped.Add(Loc.Get("Gpg_ImportNothingNew"));
        }

        return new OpenPgpImportResult(imported, updated, skipped);
    }

    public bool Delete(string fingerprint)
    {
        string normalized = NormalizeFingerprint(fingerprint);

        foreach (string path in new[]
                 {
                     PublicPath(normalized),
                     SecretPath(normalized),
                     NotePath(normalized),
                 })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The armored public key, as stored.</summary>
    public string GetPublicArmor(string fingerprint) => File.ReadAllText(PublicPath(NormalizeFingerprint(fingerprint)));

    /// <summary>The armored secret key, or <c>null</c> when this store only holds the public half.</summary>
    public string? GetSecretArmor(string fingerprint)
    {
        string path = SecretPath(NormalizeFingerprint(fingerprint));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool HasSecretKey(string fingerprint) => File.Exists(SecretPath(NormalizeFingerprint(fingerprint)));

    /// <summary>Uppercase hex, spaces and separators removed.</summary>
    public static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(" ", string.Empty).Replace(":", string.Empty).Trim().ToUpperInvariant();

    private void Store(
        string fingerprint,
        string publicArmor,
        string? secretArmor,
        string? note,
        List<string> imported,
        List<string> updated)
    {
        bool existed = File.Exists(PublicPath(fingerprint));

        File.WriteAllText(PublicPath(fingerprint), publicArmor, Encoding.ASCII);

        if (secretArmor is not null)
        {
            File.WriteAllText(SecretPath(fingerprint), secretArmor, Encoding.ASCII);
        }

        if (!string.IsNullOrWhiteSpace(note) && !File.Exists(NotePath(fingerprint)))
        {
            File.WriteAllText(NotePath(fingerprint), note, Encoding.UTF8);
        }

        if (existed)
        {
            updated.Add(fingerprint);
        }
        else
        {
            imported.Add(fingerprint);
        }
    }

    private OpenPgpStoredKey Describe(string fingerprint)
    {
        string armor = File.ReadAllText(PublicPath(fingerprint));
        OpenPgpPublicKey key = OpenPgp.ReadPublicKey(armor);
        string? secretArmor = GetSecretArmor(fingerprint);

        bool protectedSecret = false;
        if (secretArmor is not null)
        {
            protectedSecret = OpenPgp.ReadSecretKey(secretArmor).IsProtected;
        }

        return new OpenPgpStoredKey(
            key.Fingerprint,
            key.KeyId,
            key.UserId,
            key.Algorithm,
            key.KeySize,
            key.Created,
            key.Expires,
            HasSecretKey: secretArmor is not null,
            IsSecretProtected: protectedSecret,
            key.IsRevoked,
            key.CanEncrypt,
            key.CanSign,
            ReadNote(fingerprint),
            armor);
    }

    private string? ReadNote(string fingerprint)
    {
        string path = NotePath(fingerprint);
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<PgpSecretKeyRing> ReadSecretRings(string content)
    {
        PgpSecretKeyRingBundle? bundle = TryRead(() =>
        {
            using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.UTF8.GetBytes(content)));
            return new PgpSecretKeyRingBundle(stream);
        });

        if (bundle is null)
        {
            yield break;
        }

        foreach (PgpSecretKeyRing ring in bundle.GetKeyRings())
        {
            yield return ring;
        }
    }

    private static IEnumerable<PgpPublicKeyRing> ReadPublicRings(string content)
    {
        // Secret key material decodes as a public ring too, and its public half is already stored by
        // the secret pass, so only rings without a secret counterpart are taken from here.
        PgpPublicKeyRingBundle? bundle = TryRead(() =>
        {
            using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.UTF8.GetBytes(content)));
            return new PgpPublicKeyRingBundle(stream);
        });

        if (bundle is null)
        {
            yield break;
        }

        foreach (PgpPublicKeyRing ring in bundle.GetKeyRings())
        {
            yield return ring;
        }
    }

    private static T? TryRead<T>(Func<T> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string Armor(Action<Stream> encode)
    {
        using var output = new MemoryStream();
        using (var armored = new ArmoredOutputStream(output))
        {
            encode(armored);
        }

        return Encoding.ASCII.GetString(output.ToArray());
    }

    private string PublicPath(string fingerprint) => Path.Combine(Directory, fingerprint + PublicSuffix);

    private string SecretPath(string fingerprint) => Path.Combine(Directory, fingerprint + SecretSuffix);

    private string NotePath(string fingerprint) => Path.Combine(Directory, fingerprint + NoteSuffix);
}
