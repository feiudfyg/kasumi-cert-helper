using System.Text;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;

namespace KasumiCertHelper.Core.Tests;

/// <summary>
/// Conformance with GnuPG's v5 keys (the crypto-refresh draft, which GnuPG still writes for Ed448/X448).
/// The fixtures were generated with gpg 2.5.24 and use the test passphrase "kasumi-test".
/// </summary>
public sealed class V5FixtureTests
{
    private static string Directory => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string Text(string name) => File.ReadAllText(Path.Combine(Directory, name));

    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(Directory, name));

    private static string PublicKey => Text("v5-ed448-public.asc");

    private static string SecretKey => Text("v5-ed448-secret.asc");

    [Fact]
    public void ReadsTheV5PublicKey()
    {
        OpenPgpPublicKey key = OpenPgp.ReadPublicKey(PublicKey);

        Assert.Equal("AB11FCE419EA09B7983C5DBBEB5EEA1497C1EA4E2018D01519700E7C91674E58", key.Fingerprint);
        Assert.Contains("Kasumi v5 Fixture", key.UserId, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheV5SecretKey()
    {
        OpenPgpSecretKey key = OpenPgp.ReadSecretKey(SecretKey);
        Assert.True(key.IsProtected);
        Assert.Equal("AB11FCE419EA09B7983C5DBBEB5EEA1497C1EA4E2018D01519700E7C91674E58", key.Fingerprint);
    }

    [Fact]
    public void VerifiesAV5Signature()
    {
        byte[] data = Bytes("v5-ed448-data.txt");
        byte[] signature = OpenPgp.DecodeArmor(Text("v5-ed448-detached.asc"));

        OpenPgpVerification verification = OpenPgp.Verify(data, signature, PublicKey);

        Assert.True(verification.IsValid, verification.Summary);
        Assert.Contains("Kasumi v5 Fixture", verification.SignerUserId ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void DecryptsAV5Message()
    {
        byte[] message = OpenPgp.DecodeArmor(Text("v5-ed448-message.asc"));

        byte[] plaintext = OpenPgp.Decrypt(message, SecretKey, "kasumi-test");

        Assert.Equal(Bytes("v5-ed448-data.txt"), plaintext);
        Assert.Equal(Encoding.ASCII.GetString(Bytes("v5-ed448-data.txt")), Encoding.ASCII.GetString(plaintext));
    }
}
