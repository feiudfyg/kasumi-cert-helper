using System.Collections.ObjectModel;
using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using KasumiCertHelper.ViewModels;
using KasumiCertHelper.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace KasumiCertHelper.Views;

public sealed partial class GpgPage : Page
{
    /// <summary>Resolves a resource key for XAML, see <c>{x:Bind T('Key')}</c>.</summary>
    public string T(string key) => Loc.Get(key);

    private const string ColumnsKey = "Gpg.Columns";
    private const string ListPaneKey = "Gpg.ListPaneWidth";

    private readonly ObservableCollection<GpgRow> _visible = new();
    private List<GpgKey> _all = new();
    private bool _initialized;

    public GpgPage()
    {
        InitializeComponent();
        KeyList.ItemsSource = _visible;
        ApplyStoredLayout();
        FilterBox.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(52, 250, 110, 100, 56);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Settings.GetLayoutDouble(ListPaneKey, 520), GridUnitType.Pixel);
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(ListPaneKey, ContentGrid.ColumnDefinitions[0].ActualWidth);
    }

    private void OnKeyPaneSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(320, ContentGrid.ActualWidth - 260);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(ContentGrid.ColumnDefinitions[0].ActualWidth + e.Delta.Translation.X, 280, maximum),
            GridUnitType.Pixel);
        PersistLayout();
    }

    private void OnColumnResize(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag || !int.TryParse(tag, out int index))
        {
            return;
        }

        Columns[index] = Columns[index] + e.Delta.Translation.X;
        PersistLayout();
    }

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e) => Columns.FitTo(e.NewSize.Width - 20);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        UpdateDetails(null);
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private void UpdateGpgState()
    {
        GpgService gpg = AppServices.Gpg;
        GpgInfoBar.IsOpen = !gpg.IsAvailable;

        if (!gpg.IsAvailable)
        {
            DetailStatusText.Text = Loc.Get("Gpg_DetailGpgMissing");
            return;
        }

        string origin = Loc.Get(gpg.IsBundled ? "Gpg_OriginBundled" : "Gpg_OriginSystem");
        string version = string.Empty;
        try
        {
            version = gpg.GetVersion();
        }
        catch (Exception ex)
        {
            AppServices.Log(Loc.Format("Gpg_LogReadVersionFailed", ex.Message));
        }

        DetailStatusText.Text = string.IsNullOrWhiteSpace(version)
            ? Loc.Format("Gpg_OriginLine", origin, gpg.ExecutablePath)
            : $"{version}（{origin}）";
        ToolTipService.SetToolTip(GpgInfoBar, gpg.ExecutablePath);
    }

    private async Task LoadKeysAsync()
    {
        if (!AppServices.Gpg.IsAvailable)
        {
            _all = new List<GpgKey>();
            ApplyFilter();
            return;
        }

        try
        {
            BusyRing.IsActive = true;
            GpgService gpg = AppServices.Gpg;
            _all = await Task.Run(() => gpg.ListAllKeys().ToList());
        }
        catch (Exception ex)
        {
            _all = new List<GpgKey>();
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_ErrorListKeys"), ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (KeyList is null || CountText is null)
        {
            return;
        }

        IEnumerable<GpgKey> filtered = FilterBox.SelectedIndex switch
        {
            1 => _all.Where(k => k.HasSecret),
            2 => _all.Where(k => !k.HasSecret),
            _ => _all,
        };

        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            filtered = filtered.Where(k =>
                k.PrimaryUserId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.Fingerprint.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.KeyId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.UserIds.Any(u => u.Value.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        _visible.Clear();
        foreach (GpgKey key in filtered)
        {
            _visible.Add(new GpgRow(key, Columns));
        }

        CountText.Text = _all.Count == _visible.Count
            ? Loc.Format("Gpg_KeyCountAll", _visible.Count)
            : Loc.Format("Gpg_KeyCountFiltered", _visible.Count, _all.Count);

        if (KeyEmptyState is null)
        {
            return;
        }

        KeyEmptyState.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        KeyEmptyText.Text = !AppServices.Gpg.IsAvailable
            ? Loc.Get("Gpg_NoKeysGpgMissing")
            : _all.Count == 0
                ? Loc.Get("Gpg_NoKeysEmpty")
                : Loc.Get("Gpg_NoKeysFiltered");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnKeySelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetails((KeyList.SelectedItem as GpgRow)?.Key);

    private void UpdateDetails(GpgKey? key)
    {
        if (key is null)
        {
            DetailTitle.Text = Loc.Get("Gpg_DetailsTitle");
            DetailPanel.Children.Clear();
            DetailEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = key.PrimaryUserId;
        DetailEmptyState.Visibility = Visibility.Collapsed;
        DetailPresenter.Render(DetailPanel, BuildKeySections(key));
    }

    private static List<DetailSection> BuildKeySections(GpgKey key)
    {
        var basic = new List<DetailItem>
        {
            new("Fingerprint", Loc.Get("Gpg_Row_Fingerprint"), key.GroupedFingerprint, Monospace: true),
            new("KeyId", Loc.Get("Gpg_Row_KeyId"), key.KeyId),
            new("Algorithm", Loc.Get("Gpg_HeaderAlgorithm"), key.AlgorithmText),
            new("Curve", Loc.Get("Gpg_Detail_Curve"), key.Curve),
            new("Created", Loc.Get("X509_Detail_Created"), key.Created?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty),
            new("Expires", Loc.Get("Cert_Row_NotAfter"), key.Expires is null ? Loc.Get("Gpg_NeverExpires") : key.Expires.Value.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Status", Loc.Get("Cert_Row_Status"), key.StatusText),
            new("Capabilities", Loc.Get("Gpg_Detail_Capabilities"), DescribeCapabilities(key.Capabilities)),
            new("Secret", Loc.Get("Cert_Row_PrivateKey"), Loc.Get(key.HasSecret ? "Gpg_Detail_SecretPresent" : "Gpg_Detail_SecretAbsent")),
        };

        var uids = new List<DetailItem>();
        for (int i = 0; i < key.UserIds.Count; i++)
        {
            GpgUid uid = key.UserIds[i];
            uids.Add(new DetailItem(
                "Uid" + i,
                Loc.Get(uid.IsPrimary ? "Gpg_Detail_PrimaryUid" : "Gpg_Detail_Uid") + (uid.IsPrimary ? string.Empty : (i + 1).ToString()),
                $"{uid.Value}　[{uid.ValidityText}]"));
        }

        var subkeys = new List<DetailItem>();
        for (int i = 0; i < key.Subkeys.Count; i++)
        {
            GpgSubkey subkey = key.Subkeys[i];
            var parts = new List<string>
            {
                subkey.AlgorithmText,
                Loc.Get("Gpg_Detail_CapabilitiesPrefix") + DescribeCapabilities(subkey.Capabilities),
                Loc.Get(subkey.HasSecret ? "Gpg_HasSecret" : "Gpg_PublicKeyOnly"),
                subkey.Expires is null ? Loc.Get("Gpg_NeverExpires") : Loc.Get("Gpg_Detail_ValidUntil") + subkey.Expires.Value.ToString("yyyy-MM-dd"),
            };
            subkeys.Add(new DetailItem("Sub" + i, Loc.Format("Gpg_Detail_Subkey", i + 1), string.Join(Loc.Get("Common_SentenceSeparator"), parts) + "\n" + subkey.Fingerprint, Monospace: true));
        }

        return new List<DetailSection>
        {
            new(Loc.Get("Detail_Section_Basic"), basic, Expanded: true),
            new(Loc.Get("Gpg_Detail_UidSection"), uids, Expanded: true),
            new(Loc.Get("Gpg_Detail_SubkeysSection"), subkeys),
        };
    }

    private static string DescribeCapabilities(string capabilities)
    {
        if (string.IsNullOrWhiteSpace(capabilities))
        {
            return Loc.Get("Gpg_Validity_Unknown");
        }

        var usable = new SortedSet<string>(StringComparer.Ordinal);
        var unavailable = new SortedSet<string>(StringComparer.Ordinal);

        foreach (char c in capabilities)
        {
            string? text = char.ToLowerInvariant(c) switch
            {
                'e' => Loc.Get("Gpg_Capability_Encrypt"),
                's' => Loc.Get("Gpg_Capability_Sign"),
                'c' => Loc.Get("Gpg_Capability_Certify"),
                'a' => Loc.Get("Gpg_Capability_Authenticate"),
                _ => null,
            };

            if (text is not null)
            {
                (char.IsUpper(c) ? usable : unavailable).Add(text);
            }
        }

        unavailable.ExceptWith(usable);

        var parts = new List<string>();
        if (usable.Count > 0)
        {
            parts.Add(Loc.Format("Gpg_CapabilitiesUsable", string.Join(Loc.Get("Common_ListSeparator"), usable)));
        }
        if (unavailable.Count > 0)
        {
            parts.Add(Loc.Format("Gpg_CapabilitiesUnavailable", string.Join(Loc.Get("Common_ListSeparator"), unavailable)));
        }

        return parts.Count == 0 ? capabilities : string.Join(Loc.Get("Common_SentenceSeparator"), parts);
    }

    // ---------------------------------------------------------------- context menu

    private void OnKeyRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GpgRow row)
        {
            return;
        }

        KeyList.SelectedItem = row;
        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_CopyFingerprint"), "\uE8C8", () => CopyToClipboard(row.Key.GroupedFingerprint)));
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_MenuCopyUserId"), "\uE8C8", () => CopyToClipboard(row.Key.PrimaryUserId)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_MenuExportPublic"), "\uE898", () => OnExportPublicClick(this, new RoutedEventArgs())));
        if (row.Key.HasSecret)
        {
            flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_MenuExportSecret"), "\uE72E", () => OnExportSecretClick(this, new RoutedEventArgs())));
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_DeleteKey"), "\uE74D", () => OnDeleteClick(this, new RoutedEventArgs())));

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private static MenuFlyoutItem BuildMenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnCopyFingerprintClick(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            _ = DialogService.ShowMessageAsync(Loc.Get("Gpg_CopyFingerprint"), Loc.Get("Gpg_SelectKeyFirst"));
            return;
        }

        CopyToClipboard(row.Key.GroupedFingerprint);
    }

    // ---------------------------------------------------------------- operation plumbing

    private async Task<GpgOperationReport?> RunAsync(
        string title,
        string operation,
        Func<GpgResult> action,
        string? inputPath = null,
        string? outputPath = null,
        IReadOnlyList<string>? recipients = null)
    {
        if (!AppServices.Gpg.IsAvailable)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_NotFoundTitle"), Loc.Get("Gpg_NotFoundMessageLong"));
            return null;
        }

        try
        {
            BusyRing.IsActive = true;
            GpgResult result = await Task.Run(action);
            GpgOperationReport report = GpgOutputInterpreter.Describe(result, operation, inputPath, outputPath, recipients);
            await ShowReportAsync(title, report);
            return report;
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(title + Loc.Get("Common_FailedSuffix"), ex);
            return null;
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private static async Task ShowReportAsync(string title, GpgOperationReport report)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = ResultPresenter.Create(report),
                MaxHeight = 480,
                MinWidth = 400,
            },
            PrimaryButtonText = Loc.Get("Common_Ok"),
            DefaultButton = ContentDialogButton.Primary,
        };

        await DialogService.ShowAsync(dialog);
    }

    // ---------------------------------------------------------------- key management

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private async void OnLocateGpgClick(object sender, RoutedEventArgs e)
    {
        string? path = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_ChooseGpgExe"), ".exe");
        if (path is null)
        {
            return;
        }

        AppServices.Settings.GpgExecutablePath = path;
        AppServices.Settings.Save();
        AppServices.ReinitializeGpg();
        UpdateGpgState();
        await LoadKeysAsync();
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = Loc.Get("Gpg_Gen_Name"), PlaceholderText = Loc.Get("Gpg_Gen_NameHint") };
        var emailBox = new TextBox { Header = Loc.Get("Gpg_Gen_Email") };
        var commentBox = new TextBox { Header = Loc.Get("Gpg_Gen_Comment") };
        var algorithmBox = new ComboBox
        {
            Header = Loc.Get("Gpg_HeaderAlgorithm"),
            ItemsSource = new[] { "RSA (2048/3072/4096)", "ECC (NIST/Brainpool)", "Ed25519" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var lengthBox = new NumberBox
        {
            Header = Loc.Get("Gpg_Gen_RsaKeySize"),
            Minimum = 1024,
            Maximum = 8192,
            Value = 4096,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var curveBox = new ComboBox
        {
            Header = Loc.Get("Gpg_Gen_Curve"),
            ItemsSource = new[] { "nistp256", "nistp384", "nistp521", "brainpoolP256r1", "brainpoolP384r1", "brainpoolP512r1" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
        };
        var expireBox = new TextBox { Header = Loc.Get("Gpg_Gen_Expires"), Text = "2y", PlaceholderText = Loc.Get("Gpg_Gen_ExpiresHint") };
        var passphraseBox = new PasswordBox { Header = Loc.Get("Gpg_Gen_Passphrase") };
        var subkeyCheck = new CheckBox { Content = Loc.Get("Gpg_Gen_Subkey"), IsChecked = true };

        algorithmBox.SelectionChanged += (_, _) =>
        {
            curveBox.IsEnabled = algorithmBox.SelectedIndex == 1;
            lengthBox.IsEnabled = algorithmBox.SelectedIndex != 1;
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Get("Gpg_Gen_Hint"),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(nameBox);
        panel.Children.Add(emailBox);
        panel.Children.Add(commentBox);
        panel.Children.Add(algorithmBox);
        panel.Children.Add(lengthBox);
        panel.Children.Add(curveBox);
        panel.Children.Add(expireBox);
        panel.Children.Add(passphraseBox);
        panel.Children.Add(subkeyCheck);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Gpg_Gen_Title"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = Loc.Get("Common_Generate"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(nameBox.Text))
        {
            await DialogService.ShowMessageAsync(Loc.Get("Common_InvalidInput"), Loc.Get("Gpg_Gen_NameRequired"));
            return;
        }

        var options = new GpgKeyGenerationOptions
        {
            RealName = nameBox.Text.Trim(),
            Email = emailBox.Text?.Trim() ?? string.Empty,
            Comment = commentBox.Text?.Trim() ?? string.Empty,
            Algorithm = algorithmBox.SelectedIndex switch
            {
                1 => GpgKeyAlgorithm.Ecc,
                2 => GpgKeyAlgorithm.Ed25519,
                _ => GpgKeyAlgorithm.Rsa,
            },
            KeyLength = double.IsNaN(lengthBox.Value) ? 4096 : (int)lengthBox.Value,
            SubkeyLength = double.IsNaN(lengthBox.Value) ? 4096 : (int)lengthBox.Value,
            Curve = curveBox.SelectedItem as string ?? "nistp256",
            ExpireDate = string.IsNullOrWhiteSpace(expireBox.Text) ? "2y" : expireBox.Text.Trim(),
            Passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password,
            IncludeSubkey = subkeyCheck.IsChecked == true,
        };

        GpgOperationReport? report = await RunAsync(Loc.Get("Gpg_Generate"), "generate", () => AppServices.Gpg.GenerateKey(options));
        if (report is not null && report.Success)
        {
            await LoadKeysAsync();
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> paths = await FilePickerHelper.PickOpenFilesAsync(
            Loc.Get("Common_Import"), ".asc", ".gpg", ".pgp", ".key", ".pub", ".txt");
        if (paths.Count == 0)
        {
            return;
        }

        var reports = new List<(string File, GpgOperationReport Report)>();

        foreach (string path in paths)
        {
            if (!AppServices.Gpg.IsAvailable)
            {
                break;
            }

            try
            {
                BusyRing.IsActive = true;
                string file = path;
                GpgResult result = await Task.Run(() => AppServices.Gpg.ImportKey(file));
                reports.Add((Path.GetFileName(file), GpgOutputInterpreter.Describe(result, "import", inputPath: file)));
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(Loc.Format("Gpg_ImportFileFailed", Path.GetFileName(path)), ex);
            }
            finally
            {
                BusyRing.IsActive = false;
            }
        }

        await LoadKeysAsync();

        if (reports.Count == 0)
        {
            return;
        }

        if (reports.Count == 1)
        {
            await ShowReportAsync(Loc.Get("Gpg_Import"), reports[0].Report);
            return;
        }

        var combined = new GpgOperationReport("import")
        {
            Title = Loc.Format("Gpg_ProcessedFiles", reports.Count),
            Success = reports.All(r => r.Report.Success),
            Severity = reports.All(r => r.Report.Success)
                ? GpgReportSeverity.Success
                : reports.Any(r => r.Report.Success) ? GpgReportSeverity.Warning : GpgReportSeverity.Error,
        };

        foreach ((string file, GpgOperationReport report) in reports)
        {
            string detail = report.Rows.Count == 0
                ? report.Title
                : report.Title + "：" + string.Join("，", report.Rows.Take(2).Select(r => r.Label + " " + r.Value));
            combined.With(file, detail, report.Severity);
        }

        await ShowReportAsync(Loc.Get("Gpg_Import"), combined);
    }

    private async void OnExportPublicClick(object sender, RoutedEventArgs e)
        => await ExportAsync(secret: false);

    private async void OnExportSecretClick(object sender, RoutedEventArgs e)
        => await ExportAsync(secret: true);

    private async Task ExportAsync(bool secret)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            await DialogService.ShowMessageAsync(Loc.Get(secret ? "Gpg_ExportSecret" : "Gpg_ExportPublic"), Loc.Get("Gpg_SelectKeyFirst"));
            return;
        }

        GpgKey key = row.Key;
        if (secret && !key.HasSecret)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_ExportSecret"), Loc.Get("Gpg_ErrorNoSecret"));
            return;
        }

        string? passphrase = null;
        if (secret)
        {
            passphrase = await DialogService.ShowPasswordAsync(Loc.Get("Gpg_ExportSecret"), Loc.Get("Gpg_ExportPassphraseHint"));
            if (passphrase is null)
            {
                return;
            }
        }

        string suffix = secret ? "-secret" : "-public";
        string extension = secret ? ".asc" : ".asc";
        string? path = await FilePickerHelper.PickSaveFileAsync(
            key.ShortFingerprint + suffix, Loc.Get("Common_Export"),
            (Loc.Get(secret ? "Gpg_ExportArmorSecret" : "Gpg_ExportArmorPublic"), new[] { ".asc" }),
            (Loc.Get("Gpg_ExportBinaryKey"), new[] { ".gpg" }));

        if (path is null)
        {
            return;
        }

        bool armor = path.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);
        _ = extension;

        try
        {
            BusyRing.IsActive = true;
            string target = path;
            string fingerprint = key.Fingerprint;
            string? secretPassphrase = passphrase is { Length: 0 } ? null : passphrase;
            await Task.Run(() => AppServices.Gpg.ExportKeyToFile(fingerprint, target, secret, armor, secretPassphrase));

            var report = new GpgOperationReport("export")
            {
                Title = Loc.Get(secret ? "Gpg_SecretExported" : "Gpg_PublicExported"),
                Success = true,
                Severity = GpgReportSeverity.Success,
            };
            if (secret)
            {
                report.Note(Loc.Get("Gpg_NoteProtectSecret"));
            }
            report.With(Loc.Get("Gpg_Detail_Key"), key.PrimaryUserId);
            report.With(Loc.Get("Gpg_Row_Fingerprint"), key.GroupedFingerprint);
            report.With(Loc.Get("Gpg_Row_OutputFile"), path);
            await ShowReportAsync(Loc.Get(secret ? "Gpg_ExportSecret" : "Gpg_ExportPublic"), report);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_ExportFailed"), ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (KeyList.SelectedItem is not GpgRow row)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_DeleteKey"), Loc.Get("Gpg_SelectKeyFirst"));
            return;
        }

        GpgKey key = row.Key;
        bool confirmed = await DialogService.ShowConfirmAsync(
            Loc.Get("Gpg_DeleteKey"),
            Loc.Format("Gpg_ConfirmDelete", key.PrimaryUserId, key.GroupedFingerprint) +
            (key.HasSecret ? Loc.Get("Gpg_DeleteSecretWarning") : string.Empty),
            Loc.Get("Common_Delete"));
        if (!confirmed)
        {
            return;
        }

        await RunAsync(Loc.Get("Gpg_DeleteKey"), "delete", () => AppServices.Gpg.DeleteKey(key.Fingerprint, key.HasSecret));
        await LoadKeysAsync();
    }

    // ---------------------------------------------------------------- file operations

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        List<GpgKey> recipients = _all.Where(k => k.CanEncrypt).ToList();
        if (recipients.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_EncryptFile"), Loc.Get("Gpg_ErrorNoEncryptionKey"));
            return;
        }

        string? inputPath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickFileToEncrypt"), ".*");
        if (inputPath is null)
        {
            return;
        }

        var recipientList = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemsSource = recipients,
            DisplayMemberPath = "PrimaryUserId",
            Height = 200,
        };

        var armorCheck = new CheckBox { Content = Loc.Get("Gpg_EncryptArmor"), IsChecked = true };
        var signCheck = new CheckBox { Content = Loc.Get("Gpg_EncryptSign") };
        var passphraseBox = new PasswordBox { Header = Loc.Get("Gpg_EncryptSignPassphrase") };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(new TextBlock { Text = Loc.Get("Gpg_EncryptRecipients") });
        panel.Children.Add(recipientList);
        panel.Children.Add(armorCheck);
        panel.Children.Add(signCheck);
        panel.Children.Add(passphraseBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Gpg_EncryptFile"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Common_Next"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        List<GpgKey> selected = recipientList.SelectedItems.Cast<GpgKey>().ToList();
        if (selected.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_EncryptFile"), Loc.Get("Gpg_ErrorNoRecipient"));
            return;
        }

        string extension = armorCheck.IsChecked == true ? ".asc" : ".gpg";
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(
            Path.GetFileName(inputPath) + extension, Loc.Get("Gpg_SaveEncrypted"), (Loc.Get("Gpg_EncryptFile"), new[] { extension }));
        if (outputPath is null)
        {
            return;
        }

        bool armor = armorCheck.IsChecked == true;
        bool sign = signCheck.IsChecked == true;
        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;
        List<string> fingerprints = selected.Select(k => k.Fingerprint).ToList();

        await RunAsync(
            Loc.Get("Gpg_EncryptFile"),
            "encrypt",
            () => AppServices.Gpg.EncryptFile(inputPath, outputPath, fingerprints, armor, sign, null, passphrase),
            inputPath: inputPath,
            outputPath: outputPath,
            recipients: fingerprints);
    }

    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        string? inputPath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickFileToDecrypt"), ".asc", ".gpg", ".pgp");
        if (inputPath is null)
        {
            return;
        }

        string? passphrase = await DialogService.ShowPasswordAsync(Loc.Get("Gpg_DecryptFile"), Loc.Get("Gpg_DecryptPassphraseHint"));
        if (passphrase is null)
        {
            return;
        }

        string suggested = Path.GetFileNameWithoutExtension(inputPath);
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(suggested, Loc.Get("Gpg_SaveDecrypted"), (Loc.Get("Gpg_AllFiles"), new[] { ".*" }));
        if (outputPath is null)
        {
            return;
        }

        await RunAsync(
            Loc.Get("Gpg_DecryptFile"),
            "decrypt",
            () => AppServices.Gpg.DecryptFile(inputPath, outputPath, passphrase.Length == 0 ? null : passphrase),
            inputPath: inputPath,
            outputPath: outputPath);
    }

    private async void OnSignClick(object sender, RoutedEventArgs e)
    {
        List<GpgKey> signingKeys = _all.Where(k => k.CanSign && k.HasSecret).ToList();
        if (signingKeys.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_SignFile"), Loc.Get("Gpg_ErrorNoSecretKey"));
            return;
        }

        string? inputPath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickFileToSign"), ".*");
        if (inputPath is null)
        {
            return;
        }

        var keyBox = new ComboBox
        {
            Header = Loc.Get("Gpg_SignKey"),
            ItemsSource = signingKeys,
            DisplayMemberPath = "PrimaryUserId",
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var detachedCheck = new CheckBox { Content = Loc.Get("Gpg_SignDetached"), IsChecked = true };
        var clearCheck = new CheckBox { Content = Loc.Get("Gpg_SignClearsign") };
        var passphraseBox = new PasswordBox { Header = Loc.Get("Gpg_SignPassphrase") };

        detachedCheck.Checked += (_, _) => { clearCheck.IsChecked = false; };
        clearCheck.Checked += (_, _) => { detachedCheck.IsChecked = false; };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(keyBox);
        panel.Children.Add(detachedCheck);
        panel.Children.Add(clearCheck);
        panel.Children.Add(passphraseBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Gpg_SignFile"),
            Content = panel,
            PrimaryButtonText = Loc.Get("Common_Next"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (keyBox.SelectedItem is not GpgKey signingKey)
        {
            return;
        }

        bool detached = detachedCheck.IsChecked == true;
        bool clear = clearCheck.IsChecked == true;
        string extension = detached ? ".sig" : clear ? ".asc" : ".gpg";
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(
            Path.GetFileName(inputPath) + extension, Loc.Get("Gpg_SaveSignature"), (Loc.Get("Gpg_SignFile"), new[] { extension }));
        if (outputPath is null)
        {
            return;
        }

        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;

        await RunAsync(
            Loc.Get("Gpg_SignFile"),
            "sign",
            () => AppServices.Gpg.SignFile(inputPath, outputPath, detached, armor: true, signingKey.Fingerprint, passphrase, clear),
            inputPath: inputPath,
            outputPath: outputPath);
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        string? signaturePath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickSignatureFile"), ".sig", ".asc", ".gpg", ".pgp");
        if (signaturePath is null)
        {
            return;
        }

        string? dataPath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickSignedData"), ".*");

        await RunAsync(
            Loc.Get("Gpg_VerifyResult"),
            "verify",
            () => AppServices.Gpg.VerifyFile(signaturePath, dataPath),
            inputPath: signaturePath);
    }
}
