using Windows.Storage.Pickers;

using KasumiCertHelper.Core.Localization;

namespace KasumiCertHelper.Services;

public static class FilePickerHelper
{
    public static string CertificateFilterName => Loc.Get("Filter_CertificateFiles");
    public static readonly string[] CertificateExtensions = { ".cer", ".crt", ".der", ".pem", ".pfx", ".p12", ".p7b", ".sst" };

    public static async Task<string?> PickOpenFileAsync(string commitButtonText, params string[] extensions)
    {
        nint handle = GetWindowHandle();
        if (handle == 0)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = commitButtonText,
        };
        foreach (string extension in extensions.Length == 0 ? CertificateExtensions : extensions)
        {
            picker.FileTypeFilter.Add(Normalize(extension));
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<IReadOnlyList<string>> PickOpenFilesAsync(string commitButtonText, params string[] extensions)
    {
        nint handle = GetWindowHandle();
        if (handle == 0)
        {
            return Array.Empty<string>();
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = commitButtonText,
        };
        foreach (string extension in extensions.Length == 0 ? CertificateExtensions : extensions)
        {
            picker.FileTypeFilter.Add(Normalize(extension));
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        return files.Select(f => f.Path).ToList();
    }

    public static async Task<string?> PickSaveFileAsync(string suggestedFileName, string commitButtonText, params (string Name, string[] Extensions)[] fileTypes)
    {
        nint handle = GetWindowHandle();
        if (handle == 0)
        {
            return null;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedFileName,
            CommitButtonText = commitButtonText,
        };

        if (fileTypes.Length == 0)
        {
            picker.FileTypeChoices.Add(CertificateFilterName, CertificateExtensions.ToList());
        }
        else
        {
            foreach ((string name, string[] extensions) in fileTypes)
            {
                picker.FileTypeChoices.Add(name, extensions.Select(Normalize).ToList());
            }
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickFolderAsync()
    {
        nint handle = GetWindowHandle();
        if (handle == 0)
        {
            return null;
        }

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        Windows.Storage.StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private static nint GetWindowHandle()
        => App.MainWindow is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);

    private static string Normalize(string extension)
        => extension.StartsWith('.') ? "*" + extension : "*." + extension;
}
