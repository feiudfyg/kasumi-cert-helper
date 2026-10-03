namespace KasumiCertHelper.Core.Services;

/// <summary>
/// Every file picker filter is a plain extension such as <c>.asc</c>. A wildcard pattern such as
/// <c>*.asc</c> is rejected with an invalid-argument error by the underlying file extension vector,
/// so the pattern form must never reach a picker.
/// </summary>
public static class FileFilters
{
    public static string Extension(string extension)
    {
        string value = (extension ?? string.Empty).Trim();
        if (value.StartsWith('*'))
        {
            value = value[1..];
        }

        return value.StartsWith('.') ? value : "." + value;
    }
}
