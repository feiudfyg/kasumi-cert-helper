using System.Text;
using KasumiCertHelper.Core.Models;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace KasumiCertHelper.Core.Services;

/// <summary>
/// OpenPGP done in process. The application used to drive a bundled GnuPG, which meant shipping
/// gpg.exe plus the helper programs it spawns; this engine removes those executables entirely by
/// using BouncyCastle's managed OpenPGP implementation (RFC 9580).
/// <para>
/// Keys are ASCII armored, which keeps them interchangeable with GnuPG: anything exported here can be
/// imported by gpg and the other way around.
/// </para>
/// </summary>
public static class OpenPgp
{
    private static readonly SymmetricKeyAlgorithmTag[] PreferredSymmetricAlgorithms =
    {
        SymmetricKeyAlgorithmTag.Aes256,
        SymmetricKeyAlgorithmTag.Aes192,
        SymmetricKeyAlgorithmTag.Aes128,
    };

    private static readonly HashAlgorithmTag[] PreferredHashAlgorithms =
    {
        HashAlgorithmTag.Sha512,
        HashAlgorithmTag.Sha384,
        HashAlgorithmTag.Sha256,
        HashAlgorithmTag.Sha1,
    };

    public static OpenPgpKeyPair GenerateKeyPair(OpenPgpKeyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Name) && string.IsNullOrWhiteSpace(options.Email))
        {
            throw new ArgumentException("A name or an e-mail address is required.", nameof(options));
        }

        var random = new SecureRandom();
        DateTime created = DateTime.UtcNow;

        PgpKeyPair primary = CreateKeyPair(options.Algorithm, options.KeySize, options.Curve, options.KeyVersion, created, random);
        var generator = CreateRingGenerator(primary, options, random);
        if (options.IncludeEncryptionSubkey)
        {
            generator.AddSubKey(CreateEncryptionSubkey(options, created, random));
        }

        PgpSecretKeyRing secretRing = generator.GenerateSecretKeyRing();
        PgpPublicKeyRing publicRing = generator.GeneratePublicKeyRing();

        PgpPublicKey publicKey = publicRing.GetPublicKey();
        return new OpenPgpKeyPair(
            Fingerprint(publicKey),
            KeyId(publicKey),
            UserIdOf(publicKey),
            AlgorithmName(publicKey),
            KeySize(publicKey),
            publicKey.Version,
            ToLocal(publicKey.CreationTime),
            ExpiryOf(publicKey),
            HasSecretKey: true,
            Armor(publicRing),
            Armor(secretRing));
    }

    /// <summary>Reads the first public key out of armored or binary OpenPGP data.</summary>
    public static OpenPgpPublicKey ReadPublicKey(string armor)
    {
        PgpPublicKeyRing ring = ReadPublicKeyRing(armor);
        PgpPublicKey key = ring.GetPublicKey();
        return Describe(ring, key);
    }

    public static OpenPgpSecretKey ReadSecretKey(string armor)
    {
        PgpSecretKeyRing ring = ReadSecretKeyRing(armor);
        PgpSecretKey key = ring.GetSecretKey();
        PgpPublicKey publicKey = key.PublicKey;

        return new OpenPgpSecretKey(
            Fingerprint(publicKey),
            KeyId(publicKey),
            AlgorithmName(publicKey),
            KeySize(publicKey),
            publicKey.Version,
            IsProtected: key.KeyEncryptionAlgorithm != SymmetricKeyAlgorithmTag.Null,
            armor);
    }

    /// <summary>
    /// Encrypts for every recipient. <paramref name="signWithArmor"/> adds a signature, which proves
    /// who wrote the data as well as hiding it.
    /// </summary>
    public static byte[] Encrypt(
        byte[] data,
        IReadOnlyList<string> recipients,
        bool armor,
        string? signWithArmor = null,
        string? signPassphrase = null,
        string fileName = "message.txt")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipients);

        if (recipients.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        var random = new SecureRandom();
        PgpPrivateKey? signingKey = null;
        PgpSignatureGenerator? signatureGenerator = null;

        if (!string.IsNullOrWhiteSpace(signWithArmor))
        {
            PgpSecretKeyRing ring = ReadSecretKeyRing(signWithArmor!);
            PgpSecretKey secret = SelectSigningSecretKey(ring);
            signingKey = ExtractPrivateKey(secret, signPassphrase);
            signatureGenerator = new PgpSignatureGenerator(secret.PublicKey.Algorithm, HashAlgorithmTag.Sha512);
            signatureGenerator.InitSign(PgpSignature.BinaryDocument, signingKey, random);
        }

        using var output = new MemoryStream();
        Stream target = armor ? new ArmoredOutputStream(output) : output;

        try
        {
            // None of the BouncyCastle generators implement IDisposable; closing the stream they open
            // is what finalises them (the wrapped stream calls Close on its generator).
            var encrypted = new PgpEncryptedDataGenerator(
                SymmetricKeyAlgorithmTag.Aes256, withIntegrityPacket: true, random);

            foreach (string recipient in recipients)
            {
                PgpPublicKeyRing ring = ReadPublicKeyRing(recipient);
                encrypted.AddMethod(SelectEncryptionKey(ring));
            }

            var compressed = new PgpCompressedDataGenerator(CompressionAlgorithmTag.Zip);
            using Stream encryptedStream = encrypted.Open(target, new byte[1 << 16]);
            using Stream body = compressed.Open(encryptedStream);

            if (signatureGenerator is not null)
            {
                byte[] onePass = signatureGenerator.GenerateOnePassVersion(false).GetEncoded();
                body.Write(onePass, 0, onePass.Length);
            }

            var literal = new PgpLiteralDataGenerator();
            using (Stream literalStream = literal.Open(body, PgpLiteralData.Binary, fileName, data.Length, DateTime.UtcNow))
            {
                literalStream.Write(data, 0, data.Length);
            }

            if (signatureGenerator is not null)
            {
                signatureGenerator.Update(data);
                byte[] signature = signatureGenerator.Generate().GetEncoded();
                body.Write(signature, 0, signature.Length);
            }
        }
        finally
        {
            if (armor)
            {
                target.Dispose();
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Key ids an encrypted message is addressed to, so a caller can load only the secret keys that
    /// could possibly open it. Returns an empty list when the message cannot be read.
    /// </summary>
    public static IReadOnlyList<string> RecipientKeyIds(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        try
        {
            var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(new MemoryStream(data)));
            if (factory.NextPgpObject() is not PgpEncryptedDataList list)
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>();
            foreach (PgpPublicKeyEncryptedData encrypted in list.GetEncryptedDataObjects())
            {
                ids.Add(unchecked((ulong)encrypted.KeyId).ToString("X16"));
            }

            return ids;
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Decrypts with a secret key. <paramref name="secretKeyArmor"/> may contain several keys.</summary>
    public static byte[] Decrypt(byte[] data, string secretKeyArmor, string? passphrase)
    {
        ArgumentNullException.ThrowIfNull(data);

        PgpSecretKeyRingBundle secrets = ReadSecretKeyRings(secretKeyArmor);
        var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(new MemoryStream(data)));

        if (factory.NextPgpObject() is not PgpEncryptedDataList list)
        {
            throw new InvalidOperationException("The data is not an OpenPGP encrypted message.");
        }

        foreach (PgpPublicKeyEncryptedData encrypted in list.GetEncryptedDataObjects())
        {
            PgpSecretKey? secret = FindSecretKey(secrets, encrypted.KeyId);
            if (secret is null)
            {
                continue;
            }

            PgpPrivateKey privateKey = ExtractPrivateKey(secret, passphrase);
            using Stream clear = encrypted.GetDataStream(privateKey);
            return ReadLiteralData(new PgpObjectFactory(clear));
        }

        throw new InvalidOperationException("None of the supplied secret keys can decrypt this message.");
    }

    /// <summary>
    /// Signs <paramref name="data"/>. A detached signature is returned as its own OpenPGP message,
    /// otherwise signed data is produced.
    /// </summary>
    public static byte[] Sign(
        byte[] data,
        string secretKeyArmor,
        string? passphrase,
        bool detached,
        bool armor = true)
    {
        ArgumentNullException.ThrowIfNull(data);

        PgpSecretKeyRing ring = ReadSecretKeyRing(secretKeyArmor);
        PgpSecretKey secret = SelectSigningSecretKey(ring);
        PgpPrivateKey privateKey = ExtractPrivateKey(secret, passphrase);

        var generator = new PgpSignatureGenerator(secret.PublicKey.Algorithm, HashAlgorithmTag.Sha512);
        generator.InitSign(PgpSignature.BinaryDocument, privateKey, new SecureRandom());
        generator.Update(data);
        PgpSignature signature = generator.Generate();

        using var output = new MemoryStream();
        if (armor)
        {
            using var armored = new ArmoredOutputStream(output);
            byte[] encoded = signature.GetEncoded();
            armored.Write(encoded, 0, encoded.Length);
        }
        else
        {
            byte[] encoded = signature.GetEncoded();
            output.Write(encoded, 0, encoded.Length);
        }

        return output.ToArray();
    }

    /// <summary>Verifies a detached signature over <paramref name="data"/>.</summary>
    public static OpenPgpVerification Verify(byte[] data, byte[] signature, string publicKeyArmor)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);

        PgpPublicKeyRing ring = ReadPublicKeyRing(publicKeyArmor);
        PgpSignature parsed = ReadSignature(signature);

        PgpPublicKey? key = ring.GetPublicKey(parsed.KeyId);
        if (key is null)
        {
            return new OpenPgpVerification(
                false, string.Empty, string.Empty, null,
                "The signature was made with a key that is not in the supplied public key.");
        }

        parsed.InitVerify(key);
        parsed.Update(data);
        bool valid = parsed.Verify();

        PgpPublicKey primary = ring.GetPublicKey();
        DateTime? created = parsed.GetHashedSubPackets()?.HasSignatureCreationTime() == true
            ? ToLocal(parsed.GetHashedSubPackets()!.GetSignatureCreationTime())
            : null;

        return new OpenPgpVerification(
            valid,
            UserIdOf(primary),
            Fingerprint(key),
            created,
            valid
                ? $"The signature is valid and was made by {UserIdOf(primary)}."
                : "The signature is not valid. The data or the signature was changed.");
    }

    /// <summary>Uppercase hex fingerprint without separators.</summary>
    public static string Fingerprint(PgpPublicKey key) => Convert.ToHexString(key.GetFingerprint());

    public static string KeyId(PgpPublicKey key) => unchecked((ulong)key.KeyId).ToString("X16");

    /// <summary>
    /// Every key id of the ring, primary key first. A signature can be made by any key of the ring -
    /// an imported key usually signs with a subkey - so identifying the signer has to look at all of them.
    /// </summary>
    public static IReadOnlyList<string> KeyIdsOfPublicKey(string armor)
    {
        PgpPublicKeyRing ring = ReadPublicKeyRing(armor);
        var ids = new List<string>();

        foreach (PgpPublicKey key in ring.GetPublicKeys())
        {
            ids.Add(KeyId(key));
        }

        return ids;
    }

    /// <summary>True when the payload is a bare signature packet, i.e. a detached signature.</summary>
    public static bool IsDetachedSignature(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(new MemoryStream(payload)));
            return factory.NextPgpObject() is PgpSignatureList;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Key id of the key that made the signature, 16 uppercase hex digits.</summary>
    public static string KeyIdOfSignature(byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        PgpSignature parsed = ReadSignature(signature);
        return unchecked((ulong)parsed.KeyId).ToString("X16");
    }

    /// <summary>
    /// Splits an inline signed (or signed and encrypted is not accepted here) message into the data it
    /// carries and the signature packet, so it can be checked with <see cref="Verify"/>.
    /// </summary>
    public static (byte[] Data, byte[] Signature) ReadSignedMessage(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(new MemoryStream(message)));
        PgpObject? current = factory.NextPgpObject();

        while (current is not null)
        {
            if (current is PgpCompressedData compressed)
            {
                factory = new PgpObjectFactory(compressed.GetDataStream());
                current = factory.NextPgpObject();
                continue;
            }

            if (current is PgpLiteralData literal)
            {
                byte[] data;
                using (Stream stream = literal.GetInputStream())
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    data = buffer.ToArray();
                }

                PgpObject? next = factory.NextPgpObject();
                while (next is not null and not PgpSignatureList)
                {
                    next = factory.NextPgpObject();
                }

                if (next is PgpSignatureList list && list.Count > 0)
                {
                    return (data, list[0].GetEncoded());
                }

                throw new InvalidOperationException("The message is not signed.");
            }

            current = factory.NextPgpObject();
        }

        throw new InvalidOperationException("The message does not contain any data.");
    }

    /// <summary>Turns ASCII armored OpenPGP data back into the binary packet stream.</summary>
    public static byte[] DecodeArmor(string armor)
    {
        ArgumentNullException.ThrowIfNull(armor);

        using Stream decoded = PgpUtilities.GetDecoderStream(new MemoryStream(Payload(armor)));
        using var buffer = new MemoryStream();
        decoded.CopyTo(buffer);
        return buffer.ToArray();
    }

    // ------------------------------------------------------------------ helpers

    private static PgpKeyPair CreateKeyPair(
        OpenPgpKeyAlgorithm algorithm,
        int keySize,
        string curve,
        OpenPgpKeyVersion version,
        DateTime created,
        SecureRandom random)
    {
        bool v6 = version == OpenPgpKeyVersion.V6;

        switch (algorithm)
        {
            case OpenPgpKeyAlgorithm.Ed25519:
            {
                var generator = new Ed25519KeyPairGenerator();
                generator.Init(new Ed25519KeyGenerationParameters(random));
                return Create(v6, PublicKeyAlgorithmTag.Ed25519, PublicKeyAlgorithmTag.EdDsa_Legacy,
                    generator.GenerateKeyPair(), created);
            }

            case OpenPgpKeyAlgorithm.Ed448:
            {
                if (!v6)
                {
                    throw new ArgumentException("Ed448 keys require the v6 (RFC 9580) key format.", nameof(version));
                }

                var generator = new Ed448KeyPairGenerator();
                generator.Init(new Ed448KeyGenerationParameters(random));
                return Create(v6: true, PublicKeyAlgorithmTag.Ed448, PublicKeyAlgorithmTag.Ed448,
                    generator.GenerateKeyPair(), created);
            }

            case OpenPgpKeyAlgorithm.Ecdsa:
            {
                var generator = new ECKeyPairGenerator();
                generator.Init(new ECKeyGenerationParameters(CurveOid(curve), random));
                return Create(v6, PublicKeyAlgorithmTag.ECDsa, PublicKeyAlgorithmTag.ECDsa,
                    generator.GenerateKeyPair(), created);
            }

            default:
            {
                var generator = new RsaKeyPairGenerator();
                generator.Init(new RsaKeyGenerationParameters(
                    BigInteger.ValueOf(0x10001), random, keySize <= 0 ? 3072 : keySize, 25));
                return Create(v6, PublicKeyAlgorithmTag.RsaGeneral, PublicKeyAlgorithmTag.RsaGeneral,
                    generator.GenerateKeyPair(), created);
            }
        }
    }

    /// <summary>
    /// Builds a v6 key pair (RFC 9580) or a v4 one. The two formats use different algorithm
    /// identifiers for the modern curves: v4 keeps the historic <c>EdDsa_Legacy</c>/<c>ECDH</c> tags,
    /// while v6 uses the Ed25519/X25519 identifiers RFC 9580 assigned.
    /// </summary>
    private static PgpKeyPair Create(
        bool v6,
        PublicKeyAlgorithmTag v6Tag,
        PublicKeyAlgorithmTag v4Tag,
        AsymmetricCipherKeyPair keyPair,
        DateTime created)
        => v6
            ? new PgpKeyPair(PublicKeyPacket.Version6, v6Tag, keyPair, created)
            : new PgpKeyPair(v4Tag, keyPair, created);

    /// <summary>
    /// The encryption subkey: v6 pairs the modern signing curve with its matching Montgomery curve
    /// (Ed25519/X25519, Ed448/X448), v4 keeps the classic ECDH shape.
    /// </summary>
    private static PgpKeyPair CreateEncryptionSubkey(
        OpenPgpKeyOptions options,
        DateTime created,
        SecureRandom random)
    {
        bool v6 = options.KeyVersion == OpenPgpKeyVersion.V6;

        switch (options.Algorithm)
        {
            case OpenPgpKeyAlgorithm.Rsa:
            {
                var generator = new RsaKeyPairGenerator();
                generator.Init(new RsaKeyGenerationParameters(
                    BigInteger.ValueOf(0x10001), random, options.KeySize <= 0 ? 3072 : options.KeySize, 25));
                return Create(v6, PublicKeyAlgorithmTag.RsaGeneral, PublicKeyAlgorithmTag.RsaGeneral,
                    generator.GenerateKeyPair(), created);
            }

            case OpenPgpKeyAlgorithm.Ed448:
            {
                var generator = new X448KeyPairGenerator();
                generator.Init(new X448KeyGenerationParameters(random));
                return new PgpKeyPair(PublicKeyPacket.Version6, PublicKeyAlgorithmTag.X448,
                    generator.GenerateKeyPair(), created);
            }

            case OpenPgpKeyAlgorithm.Ed25519:
            {
                var generator = new X25519KeyPairGenerator();
                generator.Init(new X25519KeyGenerationParameters(random));
                return v6
                    ? new PgpKeyPair(PublicKeyPacket.Version6, PublicKeyAlgorithmTag.X25519,
                        generator.GenerateKeyPair(), created)
                    : new PgpKeyPair(PublicKeyAlgorithmTag.ECDH, generator.GenerateKeyPair(), created);
            }

            default:
            {
                var generator = new ECKeyPairGenerator();
                generator.Init(new ECKeyGenerationParameters(CurveOid(options.Curve), random));
                return Create(v6, PublicKeyAlgorithmTag.ECDH, PublicKeyAlgorithmTag.ECDH,
                    generator.GenerateKeyPair(), created);
            }
        }
    }

    private static PgpKeyRingGenerator CreateRingGenerator(
        PgpKeyPair primary,
        OpenPgpKeyOptions options,
        SecureRandom random)
    {
        var hashed = new PgpSignatureSubpacketGenerator();
        hashed.SetKeyFlags(false, PgpKeyFlags.CanCertify | PgpKeyFlags.CanSign);
        hashed.SetPreferredSymmetricAlgorithms(false,
            PreferredSymmetricAlgorithms.Select(tag => (int)tag).ToArray());
        hashed.SetPreferredHashAlgorithms(false,
            PreferredHashAlgorithms.Select(tag => (int)tag).ToArray());
        hashed.SetPreferredCompressionAlgorithms(false, new[]
        {
            (int)CompressionAlgorithmTag.Zip,
            (int)CompressionAlgorithmTag.ZLib,
            (int)CompressionAlgorithmTag.BZip2,
        });
        hashed.SetFeature(false, 0x01); // modification detection (MDC)

        if (options.ValidDays is > 0)
        {
            hashed.SetKeyExpirationTime(false, options.ValidDays.Value * 86400L);
        }

        var unhashed = new PgpSignatureSubpacketGenerator();

        return new PgpKeyRingGenerator(
            PgpSignature.PositiveCertification,
            primary,
            UserId(options),
            SymmetricKeyAlgorithmTag.Aes256,
            HashAlgorithmTag.Sha256,
            (options.Passphrase ?? string.Empty).ToCharArray(),
            useSha1: true,
            hashed.Generate(),
            unhashed.Generate(),
            random);
    }

    private static string UserId(OpenPgpKeyOptions options)
    {
        var builder = new StringBuilder(options.Name.Trim());

        if (!string.IsNullOrWhiteSpace(options.Comment))
        {
            builder.Append(" (").Append(options.Comment!.Trim()).Append(')');
        }

        if (!string.IsNullOrWhiteSpace(options.Email))
        {
            builder.Append(" <").Append(options.Email.Trim()).Append('>');
        }

        return builder.ToString();
    }

    private static Org.BouncyCastle.Asn1.DerObjectIdentifier CurveOid(string curve) => curve switch
    {
        "P-384" => ECNamedCurveTable.GetOid("P-384"),
        "P-521" => ECNamedCurveTable.GetOid("P-521"),
        _ => ECNamedCurveTable.GetOid("P-256"),
    };

    private static string AlgorithmName(PgpPublicKey key) => key.Algorithm switch
    {
        PublicKeyAlgorithmTag.RsaGeneral or PublicKeyAlgorithmTag.RsaSign or PublicKeyAlgorithmTag.RsaEncrypt => "RSA",
        PublicKeyAlgorithmTag.EdDsa_Legacy => "EdDSA",
        PublicKeyAlgorithmTag.Ed25519 => "Ed25519",
        PublicKeyAlgorithmTag.Ed448 => "Ed448",
        PublicKeyAlgorithmTag.ECDsa => "ECDSA",
        PublicKeyAlgorithmTag.ECDH => "ECDH",
        PublicKeyAlgorithmTag.X25519 => "X25519",
        PublicKeyAlgorithmTag.X448 => "X448",
        PublicKeyAlgorithmTag.Dsa => "DSA",
        PublicKeyAlgorithmTag.ElGamalGeneral or PublicKeyAlgorithmTag.ElGamalEncrypt => "ElGamal",
        _ => key.Algorithm.ToString(),
    };

    private static int KeySize(PgpPublicKey key)
    {
        try
        {
            return key.BitStrength;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string UserIdOf(PgpPublicKey key)
    {
        foreach (string id in key.GetUserIds())
        {
            return id;
        }

        return string.Empty;
    }

    private static DateTime? ExpiryOf(PgpPublicKey key)
    {
        long seconds = key.GetValidSeconds();
        return seconds > 0 ? ToLocal(key.CreationTime).AddSeconds(seconds) : null;
    }

    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();

    private static OpenPgpPublicKey Describe(PgpPublicKeyRing ring, PgpPublicKey key)
    {
        // Capabilities come from the whole ring: a key with an encryption subkey can encrypt even
        // when the primary key itself cannot, which is the normal shape for Ed25519 keys.
        bool canEncrypt = false;
        bool canSign = false;

        foreach (PgpPublicKey candidate in ring.GetPublicKeys())
        {
            canEncrypt |= candidate.IsEncryptionKey;
            canSign |= candidate.IsMasterKey || !candidate.IsEncryptionKey;
        }

        return new OpenPgpPublicKey(
            Fingerprint(key),
            KeyId(key),
            UserIdOf(ring.GetPublicKey()),
            AlgorithmName(key),
            KeySize(key),
            key.Version,
            ToLocal(key.CreationTime),
            ExpiryOf(key),
            // BC 2.7.0 renamed this probe to HasRevocation(); the crypto-refresh fork
            // (upstream PR #525, which we build against) still exposes IsRevoked().
            key.IsRevoked(),
            canEncrypt,
            canSign,
            Armor(ring));
    }

    /// <summary>The key that actually encrypts: normally the encryption subkey.</summary>
    private static PgpPublicKey SelectEncryptionKey(PgpPublicKeyRing ring)
    {
        foreach (PgpPublicKey key in ring.GetPublicKeys())
        {
            if (key.IsEncryptionKey)
            {
                return key;
            }
        }

        throw new InvalidOperationException("The recipient key cannot encrypt.");
    }

    private static PgpSecretKey SelectSigningSecretKey(PgpSecretKeyRing ring)
    {
        foreach (PgpSecretKey key in ring.GetSecretKeys())
        {
            if (!key.PublicKey.IsEncryptionKey)
            {
                return key;
            }
        }

        return ring.GetSecretKey();
    }

    private static PgpSecretKey? FindSecretKey(PgpSecretKeyRingBundle bundle, long keyId)
    {
        foreach (PgpSecretKeyRing ring in bundle.GetKeyRings())
        {
            PgpSecretKey? key = ring.GetSecretKey(keyId);
            if (key is not null)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the matching public key ring out of a secret key ring, so importing a private key also
    /// stores a public half that can be handed around. BouncyCastle keeps the user IDs and self
    /// signatures in the public key encoding, so nothing is lost.
    /// </summary>
    internal static string PublicRingArmorFromSecret(PgpSecretKeyRing secretRing)
    {
        PgpPublicKey primary = secretRing.GetPublicKey();
        PgpPublicKeyRing publicRing = new(primary.GetEncoded());

        foreach (PgpPublicKey subkey in secretRing.GetPublicKeys().Skip(1))
        {
            publicRing = PgpPublicKeyRing.InsertPublicKey(publicRing, subkey);
        }

        return Armor(output => publicRing.Encode(output));
    }
    private static PgpPrivateKey ExtractPrivateKey(PgpSecretKey key, string? passphrase)
    {
        try
        {
            return key.ExtractPrivateKey((passphrase ?? string.Empty).ToCharArray());
        }
        catch (PgpException exception)
        {
            throw new System.Security.Cryptography.CryptographicException(
                "The passphrase is wrong, or the private key cannot be read.", exception);
        }
    }

    private static byte[] ReadLiteralData(PgpObjectFactory factory)
    {
        PgpObject? current = factory.NextPgpObject();

        while (current is not null)
        {
            switch (current)
            {
                case PgpCompressedData compressed:
                    return ReadLiteralData(new PgpObjectFactory(compressed.GetDataStream()));
                case PgpLiteralData literal:
                    using (Stream stream = literal.GetInputStream())
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        return buffer.ToArray();
                    }
            }

            current = factory.NextPgpObject();
        }

        throw new InvalidOperationException("The message does not contain any data.");
    }

    private static PgpSignature ReadSignature(byte[] signature)
    {
        var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(new MemoryStream(signature)));
        PgpObject? first = factory.NextPgpObject();

        if (first is PgpSignatureList list && list.Count > 0)
        {
            return list[0];
        }

        throw new InvalidOperationException("The file does not contain an OpenPGP signature.");
    }

    private static PgpPublicKeyRing ReadPublicKeyRing(string armor)
    {
        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Payload(armor)));
        return new PgpPublicKeyRing(stream);
    }

    private static PgpSecretKeyRing ReadSecretKeyRing(string armor)
    {
        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Payload(armor)));
        return new PgpSecretKeyRing(stream);
    }

    private static PgpSecretKeyRingBundle ReadSecretKeyRings(string armor)
    {
        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Payload(armor)));
        return new PgpSecretKeyRingBundle(stream);
    }

    private static byte[] Payload(string text) => Encoding.UTF8.GetBytes(text);

    private static string Armor(PgpPublicKeyRing ring) => Armor(output => ring.Encode(output));

    private static string Armor(PgpSecretKeyRing ring) => Armor(output => ring.Encode(output));

    private static string Armor(Action<Stream> encode)
    {
        using var output = new MemoryStream();
        using (var armored = new ArmoredOutputStream(output))
        {
            encode(armored);
        }

        return Encoding.ASCII.GetString(output.ToArray());
    }
}
