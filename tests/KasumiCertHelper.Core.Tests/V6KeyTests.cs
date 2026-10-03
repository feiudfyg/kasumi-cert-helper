using System.Text;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// RFC 9580 (v6) keys. GnuPG 2.5.24 does not write v6 yet, so interop against gpg is not possible
/// here; instead these tests check the shape v6 requires (32-octet fingerprints, v6 signatures and
/// the Ed25519/X25519, Ed448/X448 identifiers) and that the engine can use the keys it generates.
/// </summary>
public sealed class V6KeyTests
{
    private static OpenPgpKeyPair Generate(OpenPgpKeyAlgorithm algorithm, string? passphrase = "pw-123456")
        => OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "V6 User",
            Email = "v6@example.com",
            Algorithm = algorithm,
            KeyVersion = OpenPgpKeyVersion.V6,
            Passphrase = passphrase,
        });

    [Fact]
    public void GeneratesAProtectedV6Ed25519Key()
    {
        OpenPgpKeyPair key = Generate(OpenPgpKeyAlgorithm.Ed25519);

        Assert.Equal(6, key.Version);
        Assert.Equal("Ed25519", key.Algorithm);
        Assert.Equal(256, key.KeySize);
        // v6 fingerprints are 32 octets, i.e. 64 hex characters.
        Assert.Equal(64, key.Fingerprint.Length);
        Assert.Contains("v6@example.com", key.UserId);

        // The self-signature has to be a v6 signature.
        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(key.PublicKeyArmor)));
        var ring = new PgpPublicKeyRing(stream);
        PgpSignature selfSignature = ring.GetPublicKey().GetSignaturesForId("V6 User <v6@example.com>").First();
        Assert.Equal(SignaturePacket.Version6, selfSignature.Version);
        selfSignature.InitVerify(ring.GetPublicKey());
        Assert.True(selfSignature.VerifyCertification("V6 User <v6@example.com>", ring.GetPublicKey()));
    }

    [Fact]
    public void V6KeySignsEncryptsAndRoundTrips()
    {
        OpenPgpKeyPair key = Generate(OpenPgpKeyAlgorithm.Ed25519);
        byte[] message = Encoding.UTF8.GetBytes("hello rfc9580");

        byte[] signature = OpenPgp.Sign(message, key.SecretKeyArmor, "pw-123456", detached: true);
        OpenPgpVerification verification = OpenPgp.Verify(message, signature, key.PublicKeyArmor);
        Assert.True(verification.IsValid, verification.Summary);
        Assert.Contains("v6@example.com", verification.SignerUserId);

        byte[] encrypted = OpenPgp.Encrypt(message, new[] { key.PublicKeyArmor }, armor: true);
        Assert.Equal("hello rfc9580", Encoding.UTF8.GetString(OpenPgp.Decrypt(encrypted, key.SecretKeyArmor, "pw-123456")));
    }

    [Fact]
    public void V6KeyRoundTripsThroughTheStore()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kasumi-v6-tests", Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new OpenPgpKeyStore(directory);
            OpenPgpKeyPair generated = Generate(OpenPgpKeyAlgorithm.Ed25519);
            store.Add(generated, "v6 note");

            OpenPgpStoredKey stored = Assert.Single(store.List());
            Assert.Equal(generated.Fingerprint, stored.Fingerprint);
            Assert.Equal(6, stored.Version);
            Assert.Equal("v6", stored.VersionText);
            Assert.True(stored.HasSecretKey);
            Assert.Equal("v6 note", stored.Note);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void GeneratesAV6Ed448KeyWithAnX448Subkey()
    {
        OpenPgpKeyPair key = Generate(OpenPgpKeyAlgorithm.Ed448);

        Assert.Equal(6, key.Version);
        Assert.Equal("Ed448", key.Algorithm);
        Assert.Equal(448, key.KeySize);
        Assert.Equal(64, key.Fingerprint.Length);

        // The encryption subkey is a v6 X448 key.
        using var stream = PgpUtilities.GetDecoderStream(new MemoryStream(Encoding.ASCII.GetBytes(key.PublicKeyArmor)));
        var ring = new PgpPublicKeyRing(stream);
        PgpPublicKey[] publicKeys = ring.GetPublicKeys().ToArray();
        Assert.Equal(PublicKeyAlgorithmTag.Ed448, publicKeys[0].Algorithm);
        Assert.Equal(PublicKeyAlgorithmTag.X448, publicKeys[1].Algorithm);
        Assert.Equal(PublicKeyPacket.Version6, publicKeys[1].Version);

        byte[] message = Encoding.UTF8.GetBytes("x448 payload");
        byte[] encrypted = OpenPgp.Encrypt(message, new[] { key.PublicKeyArmor }, armor: true);
        Assert.Equal("x448 payload", Encoding.UTF8.GetString(OpenPgp.Decrypt(encrypted, key.SecretKeyArmor, "pw-123456")));
    }

    [Fact]
    public void GeneratesAV6RsaKey()
    {
        OpenPgpKeyPair key = Generate(OpenPgpKeyAlgorithm.Rsa);

        Assert.Equal(6, key.Version);
        Assert.Equal("RSA", key.Algorithm);
        Assert.Equal(64, key.Fingerprint.Length);

        byte[] message = Encoding.UTF8.GetBytes("rsa v6");
        byte[] signature = OpenPgp.Sign(message, key.SecretKeyArmor, "pw-123456", detached: true);
        Assert.True(OpenPgp.Verify(message, signature, key.PublicKeyArmor).IsValid);
    }

    [Fact]
    public void Ed448RequiresTheV6Format()
    {
        Assert.Throws<ArgumentException>(() => OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Wrong",
            Email = "wrong@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Ed448,
            KeyVersion = OpenPgpKeyVersion.V4,
        }));
    }

    [Fact]
    public void V4KeysStayAtVersion4WithShortFingerprints()
    {
        OpenPgpKeyPair key = OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "V4 User",
            Email = "v4@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Ed25519,
            KeyVersion = OpenPgpKeyVersion.V4,
        });

        Assert.Equal(4, key.Version);
        Assert.Equal(40, key.Fingerprint.Length);
    }
}
