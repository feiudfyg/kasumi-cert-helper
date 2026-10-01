using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using KasumiCertHelper.Controls;
using KasumiCertHelper.Core.Models;
using KasumiCertHelper.Core.Services;
using KasumiCertHelper.Services;
using KasumiCertHelper.ViewModels;
using KasumiCertHelper.Core.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
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
    private List<OpenPgpStoredKey> _all = new();
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

    private OpenPgpKeyStore Store => AppServices.Pgp;

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
        await LoadKeysAsync();
    }

    private async Task LoadKeysAsync()
    {
        try
        {
            BusyRing.IsActive = true;
            OpenPgpKeyStore store = Store;
            _all = await Task.Run(() => store.List().ToList());
        }
        catch (Exception ex)
        {
            _all = new List<OpenPgpStoredKey>();
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_ErrorListKeys"), ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }

        UpdateEngineState();
        ApplyFilter();
    }

    /// <summary>Shows how many keys the in-process store holds and where they live.</summary>
    private void UpdateEngineState()
    {
        DetailStatusText.Text = Loc.Format("Gpg_EngineLine", _all.Count, Store.Directory);
        ToolTipService.SetToolTip(DetailStatusText, Store.Directory);
    }

    private void ApplyFilter()
    {
        if (KeyList is null || CountText is null)
        {
            return;
        }

        IEnumerable<OpenPgpStoredKey> filtered = FilterBox.SelectedIndex switch
        {
            1 => _all.Where(k => k.HasSecretKey),
            2 => _all.Where(k => !k.HasSecretKey),
            _ => _all,
        };

        string query = SearchBox?.Text?.Trim() ?? string.Empty;
        if (query.Length > 0)
        {
            filtered = filtered.Where(k =>
                k.UserId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.Fingerprint.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                k.KeyId.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _visible.Clear();
        foreach (OpenPgpStoredKey key in filtered)
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
        KeyEmptyText.Text = _all.Count == 0
            ? Loc.Get("Gpg_NoKeysEmpty")
            : Loc.Get("Gpg_NoKeysFiltered");
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => ApplyFilter();

    private void OnKeySelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetails((KeyList.SelectedItem as GpgRow)?.Key);

    private void UpdateDetails(OpenPgpStoredKey? key)
    {
        if (key is null)
        {
            DetailTitle.Text = Loc.Get("Gpg_DetailsTitle");
            DetailPanel.Children.Clear();
            DetailEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = key.UserId;
        DetailEmptyState.Visibility = Visibility.Collapsed;
        DetailPresenter.Render(DetailPanel, BuildKeySections(key));
    }

    private static List<DetailSection> BuildKeySections(OpenPgpStoredKey key)
    {
        var basic = new List<DetailItem>
        {
            new("Fingerprint", Loc.Get("Gpg_Row_Fingerprint"), key.GroupedFingerprint, Monospace: true),
            new("KeyId", Loc.Get("Gpg_Row_KeyId"), key.KeyId),
            new("Algorithm", Loc.Get("Gpg_HeaderAlgorithm"), key.AlgorithmText),
            new("Created", Loc.Get("X509_Detail_Created"), key.Created.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Expires", Loc.Get("Cert_Row_NotAfter"), key.Expires is null ? Loc.Get("Gpg_NeverExpires") : key.Expires.Value.ToString("yyyy-MM-dd HH:mm:ss")),
            new("Status", Loc.Get("Cert_Row_Status"), key.StatusText),
            new("Capabilities", Loc.Get("Gpg_Detail_Capabilities"), key.CapabilitiesText),
            new("Secret", Loc.Get("Cert_Row_PrivateKey"), key.SecretText),
            new("Storage", Loc.Get("Gpg_Detail_Storage"), Loc.Get(key.IsSecretProtected ? "Gpg_Detail_SecretProtected" : "Gpg_Detail_SecretUnprotected")),
            new("Note", Loc.Get("X509Dlg_Comment"), key.Note ?? string.Empty),
        };

        return new List<DetailSection>
        {
            new(Loc.Get("Detail_Section_Basic"), basic, Expanded: true),
            new(Loc.Get("Gpg_Detail_PublicKeySection"), new[]
            {
                new DetailItem("PublicArmor", Loc.Get("X509_Detail_PublicKeyPem"), key.PublicKeyArmor, Monospace: true),
            }),
        };
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
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_MenuCopyUserId"), "\uE8C8", () => CopyToClipboard(row.Key.UserId)));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildMenuItem(Loc.Get("Gpg_MenuExportPublic"), "\uE898", () => OnExportPublicClick(this, new RoutedEventArgs())));
        if (row.Key.HasSecretKey)
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

    private async Task<GpgOperationReport?> RunAsync(string title, Func<GpgOperationReport> action)
    {
        try
        {
            BusyRing.IsActive = true;
            GpgOperationReport report = await Task.Run(action);
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

    private static GpgOperationReport Report(string operation, bool success, string title)
        => new(operation)
        {
            Title = title,
            Success = success,
            Severity = success ? GpgReportSeverity.Success : GpgReportSeverity.Error,
        };

    // ---------------------------------------------------------------- key management

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await LoadKeysAsync();

    private async void OnManageKeysClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(Store.Directory);
            Process.Start(new ProcessStartInfo(Store.Directory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_OpenKeyFolderFailed"), ex);
        }
    }

    private async void OnGenerateClick(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = Loc.Get("Gpg_Gen_Name"), PlaceholderText = Loc.Get("Gpg_Gen_NameHint") };
        var emailBox = new TextBox { Header = Loc.Get("Gpg_Gen_Email") };
        var commentBox = new TextBox { Header = Loc.Get("Gpg_Gen_Comment") };
        AutomationProperties.SetAutomationId(nameBox, "GpgKeyNameBox");
        AutomationProperties.SetAutomationId(emailBox, "GpgKeyEmailBox");
        AutomationProperties.SetAutomationId(commentBox, "GpgKeyCommentBox");
        var algorithmBox = new ComboBox
        {
            Header = Loc.Get("Gpg_HeaderAlgorithm"),
            ItemsSource = new[] { "Ed25519", "ECDSA (NIST)", "RSA" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var lengthBox = new NumberBox
        {
            Header = Loc.Get("Gpg_Gen_RsaKeySize"),
            Minimum = 1024,
            Maximum = 8192,
            Value = 3072,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = false,
        };
        var curveBox = new ComboBox
        {
            Header = Loc.Get("Gpg_Gen_Curve"),
            ItemsSource = new[] { "P-256", "P-384", "P-521" },
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
            lengthBox.IsEnabled = algorithmBox.SelectedIndex == 2;
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

        if (string.IsNullOrWhiteSpace(nameBox.Text) && string.IsNullOrWhiteSpace(emailBox.Text))
        {
            await DialogService.ShowMessageAsync(Loc.Get("Common_InvalidInput"), Loc.Get("Gpg_Gen_NameRequired"));
            return;
        }

        var options = new OpenPgpKeyOptions
        {
            Name = nameBox.Text?.Trim() ?? string.Empty,
            Email = emailBox.Text?.Trim() ?? string.Empty,
            Comment = commentBox.Text?.Trim() ?? string.Empty,
            Algorithm = algorithmBox.SelectedIndex switch
            {
                1 => OpenPgpKeyAlgorithm.Ecdsa,
                2 => OpenPgpKeyAlgorithm.Rsa,
                _ => OpenPgpKeyAlgorithm.Ed25519,
            },
            KeySize = double.IsNaN(lengthBox.Value) ? 3072 : (int)lengthBox.Value,
            Curve = curveBox.SelectedItem as string ?? "P-256",
            ValidDays = ParseValidity(expireBox.Text),
            Passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password,
            IncludeEncryptionSubkey = subkeyCheck.IsChecked != false,
        };

        OpenPgpKeyPair? created = null;
        GpgOperationReport? report = await RunAsync(Loc.Get("Gpg_Generate"), () =>
        {
            created = OpenPgp.GenerateKeyPair(options);
            Store.Add(created, Loc.Get("Gpg_NoteGeneratedByApp"));

            var generated = Report("generate", true, Loc.Get("Gpg_Gen_Done"));
            generated.With(Loc.Get("Gpg_Row_NewFingerprint"), created.Fingerprint);
            generated.With(Loc.Get("Gpg_Row_KeyId"), created.KeyId);
            generated.With(Loc.Get("Gpg_Row_KeyType"), created.Algorithm);
            generated.With(Loc.Get("Gpg_Detail_Storage"), Store.Directory);
            if (string.IsNullOrEmpty(options.Passphrase))
            {
                generated.Note(Loc.Get("Gpg_NoteNoPassphrase"));
            }
            return generated;
        });

        if (report is not null)
        {
            await LoadKeysAsync();
            SelectKey(created?.Fingerprint);
        }
    }

    /// <summary>Reads "2y" / "365d" / "6m" / plain days; empty or 0 means the key never expires.</summary>
    internal static int? ParseValidity(string? text)
    {
        string value = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0)
        {
            return null;
        }

        int multiplier = value[^1] switch
        {
            'y' => 365,
            'm' => 30,
            'w' => 7,
            'd' => 1,
            _ => 0,
        };

        string digits = multiplier == 0 ? value : value[..^1];
        if (!double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double amount)
            || amount <= 0)
        {
            return null;
        }

        return Math.Max(1, (int)Math.Round(amount * (multiplier == 0 ? 1 : multiplier)));
    }

    private void SelectKey(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return;
        }

        string normalized = OpenPgpKeyStore.NormalizeFingerprint(fingerprint);
        GpgRow? row = _visible.FirstOrDefault(r =>
            string.Equals(OpenPgpKeyStore.NormalizeFingerprint(r.Key.Fingerprint), normalized, StringComparison.OrdinalIgnoreCase));

        if (row is not null)
        {
            KeyList.SelectedItem = row;
            KeyList.ScrollIntoView(row);
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
            try
            {
                BusyRing.IsActive = true;
                string file = path;
                GpgOperationReport report = await Task.Run(() =>
                {
                    string content = File.ReadAllText(file);
                    OpenPgpImportResult imported = Store.Import(content, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(file)));

                    bool success = imported.ImportedCount + imported.UpdatedCount > 0;
                    var built = Report("import", success, Loc.Get(success ? "Gpg_Import_Done" : "Gpg_Import_Nothing"));
                    built.With(Loc.Get("Gpg_Row_Imported"), Loc.Format("Gpg_Value_Count", imported.Imported));
                    built.With(Loc.Get("Gpg_Row_Unchanged"), Loc.Format("Gpg_Value_Count", imported.Updated));
                    if (imported.SkippedCount > 0)
                    {
                        built.With(Loc.Get("Gpg_Row_NotImported"), Loc.Format("Gpg_Value_Count", imported.Skipped), GpgReportSeverity.Warning);
                        built.Note(Loc.Get("Gpg_NoteImportSkipped"));
                    }
                    return built;
                });

                reports.Add((Path.GetFileName(path), report));
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

        var combined = Report("import", reports.All(r => r.Report.Success), Loc.Format("Gpg_ProcessedFiles", reports.Count));
        combined.Severity = reports.All(r => r.Report.Success)
            ? GpgReportSeverity.Success
            : reports.Any(r => r.Report.Success) ? GpgReportSeverity.Warning : GpgReportSeverity.Error;

        foreach ((string file, GpgOperationReport report) in reports)
        {
            combined.With(file, report.Title, report.Severity);
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

        OpenPgpStoredKey key = row.Key;
        if (secret && !key.HasSecretKey)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_ExportSecret"), Loc.Get("Gpg_ErrorNoSecret"));
            return;
        }

        string suffix = secret ? "-secret" : "-public";
        string? path = await FilePickerHelper.PickSaveFileAsync(
            key.ShortFingerprint + suffix, Loc.Get("Common_Export"),
            (Loc.Get(secret ? "Gpg_ExportArmorSecret" : "Gpg_ExportArmorPublic"), new[] { ".asc" }),
            (Loc.Get("Gpg_ExportBinaryKey"), new[] { ".gpg" }));

        if (path is null)
        {
            return;
        }

        try
        {
            BusyRing.IsActive = true;
            string target = path;
            string fingerprint = key.Fingerprint;
            bool armor = path.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                string text = secret
                    ? Store.GetSecretArmor(fingerprint) ?? throw new InvalidOperationException(Loc.Get("Gpg_ErrorNoSecret"))
                    : Store.GetPublicArmor(fingerprint);

                if (armor)
                {
                    File.WriteAllText(target, text);
                }
                else
                {
                    File.WriteAllBytes(target, OpenPgp.DecodeArmor(text));
                }
            });

            var report = Report("export", true, Loc.Get(secret ? "Gpg_SecretExported" : "Gpg_PublicExported"));
            if (secret)
            {
                report.Note(Loc.Get(key.IsSecretProtected ? "Gpg_NoteProtectSecret" : "Gpg_NoteUnprotectedSecret"));
            }
            report.With(Loc.Get("Gpg_Detail_Key"), key.UserId);
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

        OpenPgpStoredKey key = row.Key;
        bool confirmed = await DialogService.ShowConfirmAsync(
            Loc.Get("Gpg_DeleteKey"),
            Loc.Format("Gpg_ConfirmDelete", key.UserId, key.GroupedFingerprint) +
            (key.HasSecretKey ? Loc.Get("Gpg_DeleteSecretWarning") : string.Empty),
            Loc.Get("Common_Delete"));
        if (!confirmed)
        {
            return;
        }

        GpgOperationReport? report = await RunAsync(Loc.Get("Gpg_DeleteKey"), () =>
        {
            bool removed = Store.Delete(key.Fingerprint);
            var built = Report("delete", removed, Loc.Get(removed ? "Gpg_DeleteDone" : "Gpg_DeleteFailed"));
            built.With(Loc.Get("Gpg_Detail_Key"), key.UserId);
            return built;
        });

        if (report is not null)
        {
            await LoadKeysAsync();
        }
    }

    // ---------------------------------------------------------------- file operations

    private async void OnEncryptClick(object sender, RoutedEventArgs e)
    {
        List<OpenPgpStoredKey> recipients = _all.Where(k => k.CanEncrypt).ToList();
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
            DisplayMemberPath = "UserId",
            Height = 200,
        };

        var armorCheck = new CheckBox { Content = Loc.Get("Gpg_EncryptArmor"), IsChecked = true };
        var signCheck = new CheckBox { Content = Loc.Get("Gpg_EncryptSign") };
        var signKeyBox = new ComboBox
        {
            Header = Loc.Get("Gpg_SignKey"),
            ItemsSource = _all.Where(k => k.HasSecretKey && k.CanSign).ToList(),
            DisplayMemberPath = "UserId",
            SelectedIndex = 0,
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var passphraseBox = new PasswordBox { Header = Loc.Get("Gpg_EncryptSignPassphrase"), IsEnabled = false };
        signCheck.Checked += (_, _) => { signKeyBox.IsEnabled = true; passphraseBox.IsEnabled = true; };
        signCheck.Unchecked += (_, _) => { signKeyBox.IsEnabled = false; passphraseBox.IsEnabled = false; };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(new TextBlock { Text = Loc.Get("Gpg_EncryptRecipients"), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(recipientList);
        panel.Children.Add(armorCheck);
        panel.Children.Add(signCheck);
        panel.Children.Add(signKeyBox);
        panel.Children.Add(passphraseBox);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("Gpg_EncryptFile"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = Loc.Get("Common_Next"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        List<OpenPgpStoredKey> selected = recipientList.SelectedItems.Cast<OpenPgpStoredKey>().ToList();
        if (selected.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_EncryptFile"), Loc.Get("Gpg_ErrorNoRecipient"));
            return;
        }

        bool sign = signCheck.IsChecked == true;
        OpenPgpStoredKey? signingKey = signKeyBox.SelectedItem as OpenPgpStoredKey;

        if (sign && signingKey is null)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_EncryptFile"), Loc.Get("Gpg_ErrorNoSecretKey"));
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
        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;
        List<string> recipientArmor = selected.Select(k => k.PublicKeyArmor).ToList();
        string? signArmor = signingKey is null ? null : Store.GetSecretArmor(signingKey.Fingerprint);
        string fileName = Path.GetFileName(inputPath);

        await RunAsync(Loc.Get("Gpg_EncryptFile"), () =>
        {
            byte[] data = File.ReadAllBytes(inputPath);
            byte[] encrypted = OpenPgp.Encrypt(data, recipientArmor, armor, signArmor, passphrase, fileName);
            File.WriteAllBytes(outputPath, encrypted);

            var report = Report("encrypt", true, Loc.Get("Gpg_Encrypt_Done"));
            report.With(Loc.Get("Gpg_Row_InputFile"), fileName);
            report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);
            foreach (OpenPgpStoredKey recipient in selected)
            {
                report.With(Loc.Get("Gpg_Row_RecipientKey"), recipient.ShortFingerprint);
            }
            report.With(Loc.Get("Gpg_Row_RecipientCount"), selected.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            report.With(Loc.Get("Gpg_Row_AlsoSigned"), Loc.Get(sign ? "Common_Yes" : "Common_No"));
            return report;
        });
    }

    private async void OnDecryptClick(object sender, RoutedEventArgs e)
    {
        string? inputPath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickFileToDecrypt"), ".asc", ".gpg", ".pgp");
        if (inputPath is null)
        {
            return;
        }

        // Every secret key is offered to the engine, which picks the one the message was made for.
        string secrets = string.Concat(_all
            .Where(k => k.HasSecretKey)
            .Select(k => Store.GetSecretArmor(k.Fingerprint))
            .Where(armor => !string.IsNullOrEmpty(armor)));

        if (secrets.Length == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_DecryptFile"), Loc.Get("Gpg_ErrorNoSecretKey"));
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

        string? secret = passphrase.Length == 0 ? null : passphrase;

        await RunAsync(Loc.Get("Gpg_DecryptFile"), () =>
        {
            byte[] data = OpenPgp.Decrypt(File.ReadAllBytes(inputPath), secrets, secret);
            File.WriteAllBytes(outputPath, data);

            var report = Report("decrypt", true, Loc.Get("Gpg_Decrypt_Done"));
            report.With(Loc.Get("Gpg_Row_InputFile"), Path.GetFileName(inputPath));
            report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);
            report.With(Loc.Get("Gpg_Row_DataLength"), Loc.Format("Gpg_Value_Bytes", data.Length));
            return report;
        });
    }

    private async void OnSignClick(object sender, RoutedEventArgs e)
    {
        List<OpenPgpStoredKey> signingKeys = _all.Where(k => k.HasSecretKey && k.CanSign).ToList();
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
            DisplayMemberPath = "UserId",
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var detachedCheck = new CheckBox { Content = Loc.Get("Gpg_SignDetached"), IsChecked = true };
        var passphraseBox = new PasswordBox { Header = Loc.Get("Gpg_SignPassphrase") };

        var panel = new StackPanel { Spacing = 8, MinWidth = 340 };
        panel.Children.Add(keyBox);
        panel.Children.Add(detachedCheck);
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

        if (keyBox.SelectedItem is not OpenPgpStoredKey signingKey)
        {
            return;
        }

        bool detached = detachedCheck.IsChecked == true;
        string extension = detached ? ".sig" : ".asc";
        string? outputPath = await FilePickerHelper.PickSaveFileAsync(
            Path.GetFileName(inputPath) + extension, Loc.Get("Gpg_SaveSignature"), (Loc.Get("Gpg_SignFile"), new[] { extension }));
        if (outputPath is null)
        {
            return;
        }

        string? passphrase = string.IsNullOrEmpty(passphraseBox.Password) ? null : passphraseBox.Password;
        string? secretArmor = Store.GetSecretArmor(signingKey.Fingerprint);
        if (secretArmor is null)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Gpg_SignFile"), Loc.Get("Gpg_ErrorNoSecretKey"));
            return;
        }

        await RunAsync(Loc.Get("Gpg_SignFile"), () =>
        {
            byte[] data = File.ReadAllBytes(inputPath);
            byte[] signed = OpenPgp.Sign(data, secretArmor, passphrase, detached);
            File.WriteAllBytes(outputPath, signed);

            var report = Report("sign", true, Loc.Get("Gpg_Sign_Done"));
            report.With(Loc.Get("Gpg_Row_InputFile"), Path.GetFileName(inputPath));
            report.With(Loc.Get("Gpg_Row_OutputFile"), outputPath);
            report.With(Loc.Get("Gpg_Row_Signer"), signingKey.UserId);
            report.With(Loc.Get("Gpg_Row_SignatureType"), Loc.Get(detached ? "Gpg_SigTypeDetached" : "Gpg_SigTypeInline"));
            report.With(Loc.Get("Gpg_Row_HashAlgorithm"), "SHA-512");
            return report;
        });
    }

    private async void OnVerifyClick(object sender, RoutedEventArgs e)
    {
        string? signaturePath = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickSignatureFile"), ".sig", ".asc", ".gpg", ".pgp");
        if (signaturePath is null)
        {
            return;
        }

        byte[] payload;
        try
        {
            payload = await File.ReadAllBytesAsync(signaturePath);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Gpg_VerifyResult"), ex);
            return;
        }

        byte[]? data = null;
        byte[] signature;

        if (OpenPgp.IsDetachedSignature(payload))
        {
            signature = payload;

            // A detached signature normally sits next to the file it signs, so offer that first.
            string guess = signaturePath[..^Path.GetExtension(signaturePath).Length];
            string? dataPath = File.Exists(guess)
                ? guess
                : await FilePickerHelper.PickOpenFileAsync(Loc.Get("Gpg_PickSignedData"), ".*");

            if (dataPath is null)
            {
                return;
            }

            data = await File.ReadAllBytesAsync(dataPath);
        }
        else
        {
            try
            {
                (data, signature) = OpenPgp.ReadSignedMessage(payload);
            }
            catch (Exception ex)
            {
                await DialogService.ShowErrorAsync(Loc.Get("Gpg_VerifyResult"), ex);
                return;
            }
        }

        string keyId = OpenPgp.KeyIdOfSignature(signature);
        OpenPgpStoredKey? signer = _all.FirstOrDefault(k =>
            string.Equals(k.KeyId, keyId, StringComparison.OrdinalIgnoreCase));

        if (signer is null)
        {
            await DialogService.ShowMessageAsync(
                Loc.Get("Gpg_VerifyResult"),
                Loc.Format("Gpg_VerifyUnknownKey", keyId));
            return;
        }

        byte[] verifiedData = data;
        byte[] verifiedSignature = signature;
        OpenPgpStoredKey signerKey = signer;

        await RunAsync(Loc.Get("Gpg_VerifyResult"), () =>
        {
            OpenPgpVerification verification = OpenPgp.Verify(verifiedData, verifiedSignature, signerKey.PublicKeyArmor);

            var report = Report("verify", verification.IsValid, Loc.Get(verification.IsValid ? "Gpg_Verify_Good" : "Gpg_Verify_Bad"));
            report.Severity = verification.IsValid ? GpgReportSeverity.Success : GpgReportSeverity.Error;
            report.With(Loc.Get("Gpg_Row_Signer"), verification.SignerUserId);
            report.With(Loc.Get("Gpg_Row_Fingerprint"), verification.SignerFingerprint);
            report.With(Loc.Get("Gpg_Row_KeyId"), verifiedSignature.Length > 0 ? keyId : string.Empty);
            if (verification.Created is not null)
            {
                report.With(Loc.Get("Gpg_Row_SignatureTime"), verification.Created.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            report.With(Loc.Get("Gpg_Row_DataLength"), Loc.Format("Gpg_Value_Bytes", verifiedData.Length));
            report.Note(verification.Summary);
            return report;
        });
    }
}
