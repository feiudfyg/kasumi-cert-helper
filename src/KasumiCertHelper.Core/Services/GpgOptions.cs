namespace KasumiCertHelper.Core.Services;

public enum GpgKeyAlgorithm
{
    Rsa,
    Ecc,
    Ed25519,
}

public sealed class GpgKeyGenerationOptions
{
    public string RealName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Comment { get; set; } = string.Empty;

    public GpgKeyAlgorithm Algorithm { get; set; } = GpgKeyAlgorithm.Rsa;

    public int KeyLength { get; set; } = 4096;

    public int SubkeyLength { get; set; } = 4096;

    public string Curve { get; set; } = "nistp256";

    public string ExpireDate { get; set; } = "2y";

    public string? Passphrase { get; set; }

    public bool IncludeSubkey { get; set; } = true;
}

public sealed class GpgResult
{
    public GpgResult(int exitCode, string standardOutput, string standardError, string commandLine = "")
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        CommandLine = commandLine;
    }

    public int ExitCode { get; }

    public string CommandLine { get; }

    public string StandardOutput { get; }

    public string StandardError { get; }

    public bool Success => ExitCode == 0;

    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardOutput + Environment.NewLine + StandardError;
}
