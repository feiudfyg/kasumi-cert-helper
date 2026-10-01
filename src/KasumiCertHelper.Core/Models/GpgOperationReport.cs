namespace KasumiCertHelper.Core.Models;

public enum GpgReportSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public sealed record GpgReportRow(string Label, string Value, GpgReportSeverity Severity = GpgReportSeverity.Info);

/// <summary>
/// A human friendly interpretation of a raw gpg invocation. The raw streams are kept so
/// power users can still inspect exactly what gpg printed.
/// </summary>
public sealed class GpgOperationReport
{
    public GpgOperationReport(string operation)
    {
        Operation = operation;
    }

    public string Operation { get; }

    public string Title { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public GpgReportSeverity Severity { get; set; } = GpgReportSeverity.Info;

    public bool Success { get; set; }

    public int ExitCode { get; set; }

    public List<GpgReportRow> Rows { get; } = new();

    public List<string> Notes { get; } = new();

    public string CommandLine { get; set; } = string.Empty;

    public string RawOutput { get; set; } = string.Empty;

    public bool HasRawOutput => !string.IsNullOrWhiteSpace(RawOutput);

    public bool HasNotes => Notes.Count > 0;

    public GpgOperationReport With(string label, string? value, GpgReportSeverity severity = GpgReportSeverity.Info)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Rows.Add(new GpgReportRow(label, value.Trim(), severity));
        }
        return this;
    }

    public GpgOperationReport Note(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Notes.Add(text.Trim());
        }
        return this;
    }
}
