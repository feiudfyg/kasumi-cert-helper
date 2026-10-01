using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
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

public sealed partial class X509Page : Page
{
    /// <summary>Resolves a resource key for XAML, see <c>{x:Bind T('Key')}</c>.</summary>
    public string T(string key) => Loc.Get(key);

    private const string ColumnsKey = "X509.Columns";
    private const string ListPaneKey = "X509.ListPaneWidth";

    private readonly ObservableCollection<X509Row> _visibleItems = new();
    private X509Database? _attached;

    public X509Page()
    {
        InitializeComponent();
        ItemList.ItemsSource = _visibleItems;
        ApplyStoredLayout();
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(58, 200, 80, 70);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Settings.GetLayoutDouble(ListPaneKey, 420), GridUnitType.Pixel);
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(ListPaneKey, ContentGrid.ColumnDefinitions[0].ActualWidth);
    }

    private void OnItemPaneSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(300, ContentGrid.ActualWidth - 260);
        ContentGrid.ColumnDefinitions[0].Width = new GridLength(
            Math.Clamp(ContentGrid.ColumnDefinitions[0].ActualWidth + e.Delta.Translation.X, 260, maximum),
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

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachDatabase();
    }

    private bool EnsureDatabase()
    {
        if (AppServices.Database is null)
        {
            _ = DialogService.ShowMessageAsync(Loc.Get("X509_NoDatabase"), Loc.Get("X509_ErrorOpenDatabaseFirst"));
            return false;
        }
        return true;
    }

    private void AttachDatabase()
    {
        if (_attached is not null)
        {
            _attached.Items.CollectionChanged -= OnDatabaseItemsChanged;
        }

        X509Database? database = AppServices.Database;
        _attached = database;

        if (database is not null)
        {
            database.Items.CollectionChanged += OnDatabaseItemsChanged;
            DatabaseTitle.Text = database.Name;
            DatabaseSubtitle.Text = database.FilePath;
        }
        else
        {
            DatabaseTitle.Text = Loc.Get("X509_NoDatabase");
            DatabaseSubtitle.Text = string.IsNullOrWhiteSpace(AppServices.Settings.LastDatabasePath)
                ? Loc.Get("X509_NoDatabaseHint")
                : Loc.Get("Settings_LastDatabasePrefix") + AppServices.Settings.LastDatabasePath;
        }

        ApplyItemFilter();
        UpdateDetails(null);
    }

    private void OnDatabaseItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ApplyItemFilter();

    private void ApplyItemFilter()
    {
        string query = ItemFilterBox?.Text?.Trim() ?? string.Empty;
        X509Database? database = AppServices.Database;

        _visibleItems.Clear();
        if (database is null)
        {
            ItemCountText.Text = string.Empty;
            ItemEmptyText.Text = Loc.Get("X509_NoDatabaseYet");
            ItemEmptyState.Visibility = Visibility.Visible;
            return;
        }

        IEnumerable<X509Item> items = database.Items;
        if (query.Length > 0)
        {
            items = items.Where(i =>
                (i.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Subject?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.Comment?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (i.KindText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (X509Item item in items)
        {
            _visibleItems.Add(new X509Row(item, Columns));
        }

        ItemCountText.Text = _visibleItems.Count == database.Items.Count
            ? Loc.Format("X509_ItemCountAll", _visibleItems.Count)
            : Loc.Format("X509_ItemCountFiltered", _visibleItems.Count, database.Items.Count);

        ItemEmptyState.Visibility = _visibleItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemEmptyText.Text = database.Items.Count == 0
            ? Loc.Get("X509_EmptyItemsText")
            : Loc.Get("X509_NoMatchingItems");
    }

    private void OnItemFilterChanged(object sender, TextChangedEventArgs e) => ApplyItemFilter();

    private void OnCloseDatabaseClick(object sender, RoutedEventArgs e)
    {
        AppServices.SetDatabase(null);
        AttachDatabase();
    }

    private void RefreshItems()
    {
        ApplyItemFilter();
        UpdateDetails((ItemList.SelectedItem as X509Row)?.Item);
    }

    private void OnItemSelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateDetails((ItemList.SelectedItem as X509Row)?.Item);

    private void OnItemRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not X509Row row)
        {
            return;
        }

        ItemList.SelectedItem = row;
        var flyout = new MenuFlyout();

        if (row.Item.Kind == X509ItemKind.Csr)
        {
            flyout.Items.Add(BuildMenuItem(Loc.Get("X509_MenuSignCsr"), "\uE70F", () => OnSignCsrClick(this, new RoutedEventArgs())));
            flyout.Items.Add(new MenuFlyoutSeparator());
        }

        flyout.Items.Add(BuildMenuItem(Loc.Get("X509_MenuCopyName"), "\uE8C8", () => CopyToClipboard(row.Item.Name)));
        flyout.Items.Add(BuildMenuItem(Loc.Get("X509_MenuExport"), "\uE898", () => OnExportClick(this, new RoutedEventArgs())));
        flyout.Items.Add(BuildMenuItem(Loc.Get("Common_Delete"), "\uE74D", () => OnDeleteClick(this, new RoutedEventArgs())));

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

    private void UpdateDetails(X509Item? item)
    {
        if (item is null)
        {
            DetailTitle.Text = Loc.Get("X509_DetailsTitle");
            DetailStatusText.Text = string.Empty;
            DetailPanel.Children.Clear();
            DetailEmptyState.Visibility = Visibility.Visible;
            return;
        }

        DetailTitle.Text = item.Name;
        DetailEmptyState.Visibility = Visibility.Collapsed;
        X509Database? database = AppServices.Database;

        try
        {
            List<DetailSection> sections = item.Kind switch
            {
                X509ItemKind.Certificate => BuildCertificateSections(item, database),
                X509ItemKind.Csr => BuildCsrSections(item, database),
                _ => BuildKeySections(item, database),
            };

            DetailStatusText.Text = item.KindText + (string.IsNullOrWhiteSpace(item.Comment) ? string.Empty : " · " + item.Comment);
            DetailPresenter.Render(DetailPanel, sections);
        }
        catch (Exception ex)
        {
            DetailStatusText.Text = Loc.Get("X509_DetailUnavailable");
            DetailPanel.Children.Clear();
            DetailPanel.Children.Add(new TextBlock
            {
                Text = DialogService.DescribeException(ex),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private static List<DetailSection> BuildCertificateSections(X509Item item, X509Database? database)
    {
        X509Certificate2? certificate = database?.GetCertificate(item);
        if (certificate is null)
        {
            return new List<DetailSection>
            {
                new(Loc.Get("X509_Detail_ErrorTitle"), new[] { new DetailItem("Error", Loc.Get("X509_Detail_Content"), Loc.Get("X509_Detail_CertificateInvalid")) }, Expanded: true),
            };
        }

        var sections = DetailPresenter.ForCertificate(CertificateSummaryBuilder.Build(certificate)).ToList();
        sections.Add(new DetailSection(Loc.Get("X509_Detail_PemSection"), new[]
        {
            new DetailItem("CertificatePem", Loc.Get("X509_Detail_CertificatePem"), item.CertificatePem ?? string.Empty, Monospace: true),
        }));

        if (item.NotAfter is not null)
        {
            sections.Add(new DetailSection(Loc.Get("X509_Detail_DatabaseRecord"), new[]
            {
                new DetailItem("Created", Loc.Get("X509_Detail_Created"), item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                new DetailItem("Comment", Loc.Get("X509Dlg_Comment"), item.Comment),
                new DetailItem("Id", Loc.Get("X509_Detail_InternalId"), item.Id, Monospace: true),
            }));
        }

        return sections;
    }

    private static List<DetailSection> BuildCsrSections(X509Item item, X509Database? database)
    {
        CertificateRequest? request = database?.GetCsrRequest(item);

        var basic = new List<DetailItem>
        {
            new("Name", Loc.Get("X509Dlg_Name"), item.Name),
            new("Subject", Loc.Get("Cert_Row_Subject"), X500Name.Format(item.Subject ?? string.Empty)),
            new("SubjectCn", Loc.Get("Cert_Row_SubjectCn"), X500Name.GetCommonName(item.Subject ?? string.Empty)),
            new("Hash", Loc.Get("Gpg_Row_HashAlgorithm"), request?.HashAlgorithm.Name ?? string.Empty),
            new("PublicKey", Loc.Get("Gpg_Row_PublicKeyAlgorithm"), request?.PublicKey.Oid.FriendlyName ?? request?.PublicKey.Oid.Value ?? string.Empty),
            new("Created", Loc.Get("X509_Detail_Created"), item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new("Comment", Loc.Get("X509Dlg_Comment"), item.Comment),
        };

        var extensions = new List<DetailItem>();
        if (request is not null)
        {
            int index = 0;
            foreach (X509Extension extension in request.CertificateExtensions)
            {
                extensions.Add(new DetailItem(
                    "CsrExt" + index++,
                    extension.Oid?.FriendlyName ?? extension.Oid?.Value ?? Loc.Get("Detail_Section_Extensions"),
                    CertificateDetailsBuilder.DescribeExtensionForDisplay(extension),
                    Monospace: true));
            }
        }

        return new List<DetailSection>
        {
            new(Loc.Get("X509_Detail_CsrSection"), basic, Expanded: true),
            new(Loc.Get("X509_Detail_CsrExtensions"), extensions),
            new(Loc.Get("X509_Detail_PemSection"), new[] { new DetailItem("CsrPem", Loc.Get("X509_Detail_CsrPem"), item.CsrPem ?? string.Empty, Monospace: true) }),
        };
    }

    private static List<DetailSection> BuildKeySections(X509Item item, X509Database? database)
    {
        string usedBy = string.Empty;
        if (database is not null)
        {
            List<X509Item> used = database.Items.Where(i => i.KeyId == item.Id).ToList();
            if (used.Count > 0)
            {
                usedBy = string.Join("；", used.Select(i => $"{i.Name}（{i.KindText}）"));
            }
        }

        var basic = new List<DetailItem>
        {
            new("Name", Loc.Get("X509Dlg_Name"), item.Name),
            new("Algorithm", Loc.Get("Gpg_HeaderAlgorithm"), item.KeyAlgorithm ?? string.Empty),
            new("KeySize", Loc.Get("Cert_Row_KeySize"), item.KeySize is null ? string.Empty : item.KeySize + " bit"),
            new("Storage", "Storage", item.HasPrivateKey ? Loc.Get("X509_Detail_StorageEncrypted") : Loc.Get("X509_Detail_StorageNone")),
            new("Created", Loc.Get("X509_Detail_Created"), item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
            new("UsedBy", Loc.Get("X509_Detail_UsedBy"), usedBy),
            new("Comment", Loc.Get("X509Dlg_Comment"), item.Comment),
        };

        return new List<DetailSection>
        {
            new(Loc.Get("X509_Kind_PrivateKey"), basic, Expanded: true),
            new(Loc.Get("X509_Detail_PublicKeySection"), new[]
            {
                new DetailItem("KeyPem", Loc.Get("X509_Detail_PublicKeyPem"), BuildKeyPem(item, database), Monospace: true),
            }),
        };
    }

    private static string BuildKeyPem(X509Item item, X509Database? database)
    {
        if (database is null)
        {
            return item.EncryptedKeyPem ?? string.Empty;
        }

        try
        {
            using AsymmetricAlgorithm key = database.LoadKey(item);
            return Loc.Get("X509_Detail_PublicKeyBanner") + Environment.NewLine +
                   key.ExportSubjectPublicKeyInfoPem() + Environment.NewLine +
                   Loc.Get("X509_Detail_PrivateKeyBanner");
        }
        catch (Exception ex)
        {
            return Loc.Get("X509_Detail_CannotReadPrivateKeyPrefix") + ex.Message + Environment.NewLine + Environment.NewLine + (item.EncryptedKeyPem ?? string.Empty);
        }
    }

    private async void OnNewDatabaseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (string Path, string Password)? input = await DatabaseDialogs.ShowCreateAsync();
            if (input is null)
            {
                return;
            }

            X509Database database = X509Database.Create(input.Value.Path, input.Value.Password);
            AppServices.SetDatabase(database);
            AppServices.AddRecentDatabase(database.FilePath);
            AttachDatabase();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorCreateDatabase"), ex);
        }
    }

    private async void OnOpenDatabaseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            (string Path, string Password)? input = await DatabaseDialogs.ShowOpenAsync(AppServices.Settings.RecentDatabases);
            if (input is null)
            {
                return;
            }

            X509Database database = X509Database.Open(input.Value.Path, input.Value.Password);
            if (!database.ValidatePassword(input.Value.Password))
            {
                await DialogService.ShowMessageAsync(Loc.Get("Common_WrongPassword"), Loc.Get("X509_ErrorWrongPassword"));
                return;
            }

            AppServices.SetDatabase(database);
            AppServices.AddRecentDatabase(database.FilePath);
            AttachDatabase();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorOpenDatabase"), ex);
        }
    }

    private async void OnNewKeyClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        KeyDialogResult? input = await X509Dialogs.ShowKeyAsync("key-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        if (input is null)
        {
            return;
        }

        try
        {
            using AsymmetricAlgorithm key = X509Factory.CreateKey(input.Key);
            database.AddKey(input.Name, key, input.Comment);
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorGenerateKey"), ex);
        }
    }

    private async void OnNewCertificateClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(false, false);

    private async void OnNewCaClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(true, false);

    private async void OnNewCsrClick(object sender, RoutedEventArgs e) => await CreateCertificateAsync(false, true);

    private async Task CreateCertificateAsync(bool isCa, bool asCsr)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        List<X509Item> keys = database.Items.Where(i => i.Kind == X509ItemKind.PrivateKey).ToList();

        string title = Loc.Get(asCsr ? "X509_TitleNewCsr" : isCa ? "X509_TitleNewCa" : "X509_TitleNewSelfSigned");
        string defaultName = isCa ? "Kasumi Root CA" : "server.example.com";

        CertificateDialogResult? input = await X509Dialogs.ShowCertificateAsync(title, defaultName, isCa, keys, null);
        if (input is null)
        {
            return;
        }

        try
        {
            AsymmetricAlgorithm key;
            X509Item keyItem;

            if (input.ExistingKeyId is not null)
            {
                keyItem = database.Items.First(i => i.Id == input.ExistingKeyId);
                key = database.LoadKey(keyItem);
            }
            else
            {
                key = X509Factory.CreateKey(input.NewKey!);
                keyItem = database.AddKey(input.Name + Loc.Get("X509_KeyNameSuffix"), key, Loc.Get("X509_CommentGeneratedByWizard"));
            }

            using (key)
            {
                X509CertificateOptions options = BuildOptions(input);
                if (asCsr)
                {
                    CertificateRequest request = X509Factory.CreateRequest(key, options);
                    database.AddCsr(input.Name, request, keyItem.Id, input.Comment);
                }
                else
                {
                    X509Certificate2 certificate = X509Factory.CreateSelfSigned(key, options);
                    database.AddCertificate(input.Name, certificate, keyItem.Id, input.Comment);
                }
            }

            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorGenerate"), ex);
        }
    }

    private static X509CertificateOptions BuildOptions(CertificateDialogResult input) => new()
    {
        Subject = input.Subject.BuildDistinguishedName(),
        ValidDays = input.ValidDays,
        HashAlgorithm = input.Hash,
        IsCa = input.IsCa,
        HasPathLengthConstraint = input.HasPathLengthConstraint,
        PathLengthConstraint = input.PathLengthConstraint,
        KeyUsage = input.KeyUsage,
        IncludeKeyUsage = true,
        ExtendedKeyUsageOids = input.ExtendedKeyUsages,
        SubjectAlternativeNames = input.SubjectAlternativeNames,
        CrlDistributionPoints = input.CrlDistributionPoints,
        OcspUrls = input.OcspUrls,
        CertificatePolicies = input.CertificatePolicies,
    };

    private async void OnSignCsrClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        List<X509Item> csrs = database.Items.Where(i => i.Kind == X509ItemKind.Csr).ToList();
        List<X509Item> cas = database.Items.Where(i => i.Kind == X509ItemKind.Certificate && i.IsCa).ToList();

        if (csrs.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("X509_SignCsr"), Loc.Get("X509_ErrorNoCsr"));
            return;
        }
        if (cas.Count == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("X509_SignCsr"), Loc.Get("X509_ErrorNoCa"));
            return;
        }

        var csrBox = new ComboBox { Header = Loc.Get("X509_NewCsr"), ItemsSource = csrs, DisplayMemberPath = "Name", MinWidth = 380 };
        csrBox.SelectedItem = ItemList.SelectedItem is X509Row { Item.Kind: X509ItemKind.Csr } selected ? selected.Item : csrs[0];

        var caBox = new ComboBox { Header = Loc.Get("X509_SignIssuerCa"), ItemsSource = cas, DisplayMemberPath = "Name", MinWidth = 380 };
        caBox.SelectedItem = cas[0];

        var daysBox = new NumberBox
        {
            Header = Loc.Get("X509Dlg_ValidityDays"),
            Minimum = 1,
            Maximum = 36500,
            Value = 365,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            MinWidth = 380,
        };

        var caHint = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Gray),
        };

        void UpdateCaHint()
        {
            if (caBox.SelectedItem is not X509Item ca)
            {
                return;
            }

            int remainingDays = ca.NotAfter is null
                ? 365
                : Math.Max(1, (int)(ca.NotAfter.Value - DateTime.Now).TotalDays);
            daysBox.Value = Math.Min(remainingDays, 365);
            caHint.Text = Loc.Format("X509_SignCaHint", ca.NotAfter?.ToString("yyyy-MM-dd") ?? string.Empty, remainingDays);
        }

        caBox.SelectionChanged += (_, _) => UpdateCaHint();
        UpdateCaHint();
        var hashBox = new ComboBox { Header = Loc.Get("X509Dlg_HashAlgorithm"), ItemsSource = X509Factory.HashAlgorithms, SelectedIndex = 0, MinWidth = 380 };
        var caCheck = new CheckBox { Content = Loc.Get("X509_SignMarkAsCa") };
        var pathLenCheck = new CheckBox { Content = Loc.Get("X509Dlg_LimitPathLength"), IsEnabled = false };
        var pathLenBox = new NumberBox { Minimum = 0, Maximum = 32, Value = 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, IsEnabled = false };
        caCheck.Checked += (_, _) => { pathLenCheck.IsEnabled = true; };
        caCheck.Unchecked += (_, _) => { pathLenCheck.IsEnabled = false; pathLenCheck.IsChecked = false; pathLenBox.IsEnabled = false; };
        pathLenCheck.Checked += (_, _) => pathLenBox.IsEnabled = true;
        pathLenCheck.Unchecked += (_, _) => pathLenBox.IsEnabled = false;

        var pathLenPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        pathLenPanel.Children.Add(pathLenCheck);
        pathLenPanel.Children.Add(pathLenBox);

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.Get("X509_SignHint"),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(csrBox);
        panel.Children.Add(caBox);
        panel.Children.Add(caHint);
        panel.Children.Add(daysBox);
        panel.Children.Add(hashBox);
        panel.Children.Add(caCheck);
        panel.Children.Add(pathLenPanel);

        var dialog = new ContentDialog
        {
            Title = Loc.Get("X509_SignDialogTitle"),
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            PrimaryButtonText = Loc.Get("X509_Sign"),
            CloseButtonText = Loc.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
        {
            return;
        }

        if (csrBox.SelectedItem is not X509Item csrItem || caBox.SelectedItem is not X509Item caItem)
        {
            return;
        }

        try
        {
            CertificateRequest? request = database.GetCsrRequest(csrItem);
            if (request is null)
            {
                await DialogService.ShowMessageAsync(Loc.Get("X509_ErrorSign"), Loc.Get("X509_ErrorInvalidCsr"));
                return;
            }

            X509Certificate2? caCertificate = database.GetCertificateWithKey(caItem);
            if (caCertificate is null || !caCertificate.HasPrivateKey)
            {
                await DialogService.ShowMessageAsync(Loc.Get("X509_ErrorSign"), Loc.Get("X509_ErrorCaNoPrivateKey"));
                return;
            }

            bool isCa = caCheck.IsChecked == true;
            var options = new X509CertificateOptions
            {
                ValidDays = double.IsNaN(daysBox.Value) ? 365 : (int)daysBox.Value,
                HashAlgorithm = hashBox.SelectedItem as string ?? "SHA256",
                IsCa = isCa,
                HasPathLengthConstraint = pathLenCheck.IsChecked == true,
                PathLengthConstraint = double.IsNaN(pathLenBox.Value) ? 0 : (int)pathLenBox.Value,
                IncludeKeyUsage = isCa,
                KeyUsage = X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                Subject = csrItem.Subject ?? string.Empty,
            };

            X509Certificate2 signed = X509Factory.SignCsr(
                X509Factory.LoadCsr(csrItem.CsrPem!, options.HashAlgorithm),
                caCertificate,
                options);

            string name = X500Name.GetCommonName(signed.Subject);
            database.AddCertificate(name, signed, csrItem.KeyId, Loc.Format("X509_CommentSignedBy", caItem.Name));
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorSign"), ex);
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        try
        {
            IReadOnlyList<string> paths = await FilePickerHelper.PickOpenFilesAsync(
                Loc.Get("Common_Import"),
                ".pem", ".crt", ".cer", ".der", ".key", ".csr", ".pfx", ".p12", ".txt", ".asc");
            if (paths.Count == 0)
            {
                return;
            }

            int imported = 0;
            var errors = new List<string>();

            foreach (string path in paths)
            {
                try
                {
                    imported += await ImportFileAsync(path);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }

            RefreshItems();

            string message = Loc.Format("X509_ImportedItems", imported);
            if (errors.Count > 0)
            {
                message += Environment.NewLine + Environment.NewLine + Loc.Get("X509_ImportFailedFiles") + Environment.NewLine + string.Join(Environment.NewLine, errors);
            }
            await DialogService.ShowMessageAsync(Loc.Get("X509_ImportDoneTitle"), message);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_ImportFailed"), ex);
        }
    }

    private async Task<int> ImportFileAsync(string path)
    {
        X509Database database = AppServices.Database!;
        byte[] data = await File.ReadAllBytesAsync(path);
        string name = Path.GetFileNameWithoutExtension(path);
        string? text = TryGetText(data);

        if (text is not null && text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            int count = 0;

            if (text.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                string? password = text.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal)
                    ? await DialogService.ShowPasswordAsync(Loc.Get("X509_PrivateKeyPassword"), Loc.Get("X509_EnterPrivateKeyPassword"))
                    : null;
                database.ImportKey(name, text, password, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                count++;
            }

            if (text.Contains("CERTIFICATE REQUEST", StringComparison.Ordinal))
            {
                database.ImportCsr(name, text, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                count++;
            }

            if (text.Contains("BEGIN CERTIFICATE", StringComparison.Ordinal))
            {
                string? keyPem = text.Contains("PRIVATE KEY", StringComparison.Ordinal) ? text : null;
                database.ImportCertificate(name, text, keyPem, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                count++;
            }

            return count;
        }

        bool looksLikePkcs12 = data.Length > 2 && data[0] == 0x30;
        if (looksLikePkcs12)
        {
            try
            {
                string? password = null;
                IReadOnlyList<X509Certificate2> certs;
                try
                {
                    certs = CertificateFileIO.Load(data, null);
                }
                catch (CryptographicException)
                {
                    password = await DialogService.ShowPasswordAsync(Loc.Get("X509_PfxPassword"), Loc.Get("X509_EnterPfxPassword"));
                    if (password is null)
                    {
                        return 0;
                    }
                    certs = CertificateFileIO.Load(data, password);
                }

                if (certs.Count > 0 && certs[0].HasPrivateKey)
                {
                    CertificateFileIO.Export(certs[0], path + ".tmp", CertificateFileFormat.Pkcs12, password, true);
                    database.ImportCertificate(name, certs[0].ExportCertificatePem(), null, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                    using AsymmetricAlgorithm? privateKey = CertificateKeyIO.GetPrivateKey(certs[0]);
                    if (privateKey is not null)
                    {
                        database.AddKey(name + Loc.Get("X509_KeyNameSuffix"), privateKey, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                    }
                    try { File.Delete(path + ".tmp"); } catch (Exception) { }
                }
                else
                {
                    foreach (X509Certificate2 certificate in certs)
                    {
                        database.ImportCertificate(name, certificate.ExportCertificatePem(), null, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                    }
                }

                return Math.Max(1, certs.Count);
            }
            catch (CryptographicException)
            {
                X509Certificate2 certificate = new(data);
                database.ImportCertificate(name, certificate.ExportCertificatePem(), null, Loc.Format("X509_CommentImportedFrom", Path.GetFileName(path)));
                return 1;
            }
        }

        throw new InvalidOperationException(Loc.Get("Error_UnrecognizedFileFormat"));
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not X509Item item)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Common_Export"), Loc.Get("X509_SelectItemFirst"));
            return;
        }

        X509Database database = AppServices.Database!;
        bool hasPrivateKey = item.HasPrivateKey || database.FindKey(item) is not null;

        ExportDialogResult? options = await X509Dialogs.ShowExportAsync(item, hasPrivateKey);
        if (options is null)
        {
            return;
        }

        try
        {
            string baseName = SanitizeFileName(item.Name);

            if (item.Kind == X509ItemKind.Certificate)
            {
                X509Certificate2? certificate = options.IncludePrivateKey
                    ? database.GetCertificateWithKey(item)
                    : database.GetCertificate(item);
                if (certificate is null)
                {
                    await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportFailed"), Loc.Get("X509_ErrorInvalidCertificate"));
                    return;
                }

                (string name, string[] extensions)[] types = options.Format switch
                {
                    0 => new[] { (Loc.Get("Stores_SavePemCert"), new[] { ".crt", ".pem" }) },
                    1 => new[] { (Loc.Get("Stores_SaveDerCert"), new[] { ".cer" }) },
                    _ => new[] { ("PKCS#12", new[] { ".pfx", ".p12" }) },
                };

                string? path = await FilePickerHelper.PickSaveFileAsync(baseName, Loc.Get("Common_Export"), types);
                if (path is null)
                {
                    return;
                }

                CertificateFileFormat format = options.Format switch
                {
                    0 => CertificateFileFormat.Pem,
                    1 => CertificateFileFormat.Der,
                    _ => CertificateFileFormat.Pkcs12,
                };

                CertificateFileIO.Export(certificate, path, format, options.Password, options.IncludePrivateKey);
                await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportSuccessTitle"), Loc.Get("Common_ExportedTo") + path);
                return;
            }

            if (item.Kind == X509ItemKind.Csr)
            {
                string? path = await FilePickerHelper.PickSaveFileAsync(baseName, Loc.Get("Common_Export"), (Loc.Get("X509_SavePemCsr"), new[] { ".csr", ".pem" }));
                if (path is null)
                {
                    return;
                }
                await File.WriteAllTextAsync(path, item.CsrPem ?? string.Empty);
                await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportSuccessTitle"), Loc.Get("Common_ExportedTo") + path);
                return;
            }

            string? keyPath = await FilePickerHelper.PickSaveFileAsync(baseName, Loc.Get("Common_Export"), (Loc.Get("X509_SavePemKey"), new[] { ".key", ".pem" }));
            if (keyPath is null)
            {
                return;
            }
            await File.WriteAllTextAsync(keyPath, database.ExportPrivateKeyPem(item, options.Password));
            await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportSuccessTitle"), Loc.Get("Common_ExportedTo") + keyPath);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_ExportFailed"), ex);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not X509Item item)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Common_Delete"), Loc.Get("X509_SelectItemFirst"));
            return;
        }

        X509Database database = AppServices.Database!;
        string extra = item.Kind == X509ItemKind.PrivateKey
            ? Loc.Get("X509_DeletePrivateKeyWarning")
            : string.Empty;

        bool confirmed = await DialogService.ShowConfirmAsync(
            Loc.Get("X509_DeleteTitle"),
            Loc.Format("X509_ConfirmDelete", item.Name, item.KindText, extra),
            Loc.Get("Common_Delete"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            database.Remove(item);
            RefreshItems();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_DeleteFailed"), ex);
        }
    }

    private async void OnChangePasswordClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureDatabase())
        {
            return;
        }

        X509Database database = AppServices.Database!;
        string? oldPassword = await DialogService.ShowPasswordAsync(Loc.Get("X509_ChangePassword"), Loc.Get("X509_CurrentPassword"));
        if (oldPassword is null)
        {
            return;
        }
        if (!database.ValidatePassword(oldPassword))
        {
            await DialogService.ShowMessageAsync(Loc.Get("Common_WrongPassword"), Loc.Get("X509_ErrorCurrentPassword"));
            return;
        }

        string? newPassword = await DialogService.ShowPasswordAsync(Loc.Get("X509_ChangePassword"), Loc.Get("X509_NewPassword"), confirmRequired: true);
        if (newPassword is null)
        {
            return;
        }
        if (newPassword.Length == 0)
        {
            await DialogService.ShowMessageAsync(Loc.Get("X509_PasswordRequiredTitle"), Loc.Get("X509_ErrorNewPasswordRequired"));
            return;
        }

        try
        {
            database.ChangePassword(oldPassword, newPassword);
            AppServices.Settings.Save();
            await DialogService.ShowMessageAsync(Loc.Get("Common_Done"), Loc.Get("X509_PasswordChanged"));
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("X509_ErrorChangePassword"), ex);
        }
    }

    private static string? TryGetText(byte[] data)
    {
        try
        {
            string text = System.Text.Encoding.UTF8.GetString(data);
            return text.Contains('\uFFFD') ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "item";
        }
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}
