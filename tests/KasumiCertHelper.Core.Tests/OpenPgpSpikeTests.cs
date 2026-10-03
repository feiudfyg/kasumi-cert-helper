using System.Text;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// Spike: proves the in-process OpenPGP engine can do everything the GPG page needs, so the
/// implementation is not written against a library that turns out not to fit.
/// </summary>
public class OpenPgpSpikeTests
{
    /// <summary>
    /// The decrypt page uses this to load only the secret keys a message could belong to, so the key
    /// ids it reports have to be the ones in the ring (an Ed25519 key encrypts through its subkey).
    /// </summary>
    [Fact]
    public void ReportsTheKeyIdsAnEncryptedMessageIsAddressedTo()
    {
        OpenPgpKeyPair key = OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Recipient",
            Email = "recipient@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Ed25519,
        });

        byte[] encrypted = OpenPgp.Encrypt(
            Encoding.UTF8.GetBytes("for a subkey"), new[] { key.PublicKeyArmor }, armor: true);

        IReadOnlyList<string> recipients = OpenPgp.RecipientKeyIds(encrypted);
        IReadOnlyList<string> ringIds = OpenPgp.KeyIdsOfPublicKey(key.PublicKeyArmor);

        Assert.NotEmpty(recipients);
        Assert.All(recipients, id => Assert.Contains(id, ringIds));
        Assert.Equal("for a subkey", Encoding.UTF8.GetString(OpenPgp.Decrypt(encrypted, key.SecretKeyArmor, null)));

        // Data that is not an encrypted message reports nothing instead of throwing.
        Assert.Empty(OpenPgp.RecipientKeyIds(Encoding.UTF8.GetBytes("plain text")));
    }
    [Fact]
    public void GeneratesEncryptsDecryptsSignsAndVerifies()
    {
        OpenPgpKeyPair key = OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Kasumi User",
            Email = "kasumi@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Ed25519,
            Passphrase = "pw-123456",
            ValidDays = 30,
        });

        Assert.Contains("kasumi@example.com", key.UserId);
        // v4 keys carry a SHA-1 fingerprint (20 bytes); gpg 2.4 emits the same by default.
        Assert.Equal(40, key.Fingerprint.Length);
        Assert.True(key.HasSecretKey);

        byte[] message = Encoding.UTF8.GetBytes("hello openpgp");

        byte[] encrypted = OpenPgp.Encrypt(message, new[] { key.PublicKeyArmor }, armor: true);
        Assert.Contains("BEGIN PGP MESSAGE", Encoding.ASCII.GetString(encrypted));

        byte[] decrypted = OpenPgp.Decrypt(encrypted, key.SecretKeyArmor, "pw-123456");
        Assert.Equal("hello openpgp", Encoding.UTF8.GetString(decrypted));

        byte[] signature = OpenPgp.Sign(message, key.SecretKeyArmor, "pw-123456", detached: true);
        OpenPgpVerification verification = OpenPgp.Verify(message, signature, key.PublicKeyArmor);
        Assert.True(verification.IsValid, verification.Summary);
        Assert.Contains("kasumi@example.com", verification.SignerUserId);
    }

    [Fact]
    public void DetectsATamperedSignature()
    {
        OpenPgpKeyPair key = OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Tamper Test",
            Email = "tamper@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Rsa,
            KeySize = 2048,
            Passphrase = "pw-123456",
        });

        byte[] message = Encoding.UTF8.GetBytes("original");
        byte[] signature = OpenPgp.Sign(message, key.SecretKeyArmor, "pw-123456", detached: true);

        OpenPgpVerification verification = OpenPgp.Verify(
            Encoding.UTF8.GetBytes("tampered"),
            signature,
            key.PublicKeyArmor);

        Assert.False(verification.IsValid);
    }

    [Fact]
    public void RoundTripsThroughArmoredKeyMaterial()
    {
        OpenPgpKeyPair key = OpenPgp.GenerateKeyPair(new OpenPgpKeyOptions
        {
            Name = "Round Trip",
            Email = "round@example.com",
            Algorithm = OpenPgpKeyAlgorithm.Ecdsa,
            Curve = "P-256",
            Passphrase = "pw-123456",
        });

        OpenPgpPublicKey parsed = OpenPgp.ReadPublicKey(key.PublicKeyArmor);
        Assert.Equal(key.Fingerprint, parsed.Fingerprint);
        Assert.Contains("round@example.com", parsed.UserId);

        OpenPgpSecretKey secret = OpenPgp.ReadSecretKey(key.SecretKeyArmor);
        Assert.True(secret.IsProtected);
        Assert.Equal(key.Fingerprint, secret.Fingerprint);
    }
}
