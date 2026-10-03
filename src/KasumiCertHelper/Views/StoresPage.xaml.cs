using System.Collections.ObjectModel;
using System.Diagnostics;
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
using Windows.System;

namespace KasumiCertHelper.Views;

public sealed partial class StoresPage : Page
{
    /// <summary>Resolves a resource key for XAML, see <c>{x:Bind T('Key')}</c>.</summary>
    public string T(string key) => Loc.Get(key);

    private const string ColumnsKey = "Stores.Columns";
    private const string StorePaneKey = "Stores.StorePaneWidth";
    private const string DetailsPaneKey = "Stores.DetailsPaneHeight";
    private const string DetailsVisibleKey = "Stores.DetailsVisible";

    /// <summary>The details pane never gets smaller than this, whether dragged or squeezed.</summary>
    private const double MinimumDetailsHeight = 140;

    /// <summary>Room the certificate list keeps when the window is too short for both panes.</summary>
    private const double MinimumListHeight = 120;

    private readonly CertificateStoreService _stores = AppServices.StoreService;
    private readonly ObservableCollection<CertRow> _visibleCertificates = new();
    private readonly ObservableCollection<StoreSummary> _userStores = new();
    private readonly ObservableCollection<StoreSummary> _machineStores = new();

    private List<CertificateItem> _allCertificates = new();
    private List<StoreSummary> _allUserStores = new();
    private List<StoreSummary> _allMachineStores = new();
    private StoreLocation _location = StoreLocation.CurrentUser;
    private string _storeName = "My";
    private CertificateSummary? _currentSummary;
    private bool _suppressSelection;
    private bool _initialized;
    private bool _detailsVisible = true;

    /// <summary>Height the user chose for the details pane, or 0 while none has been chosen.</summary>
    private double _detailsHeight;

    public StoresPage()
    {
        InitializeComponent();

        CertList.ItemsSource = _visibleCertificates;
        UserStoreList.ItemsSource = _userStores;
        MachineStoreList.ItemsSource = _machineStores;
        ApplyStoredLayout();
        Loaded += OnLoaded;
    }

    public TableColumnLayout Columns { get; } = new(56, 250, 160, 110, 64);

    private SettingsService Settings => AppServices.Settings;

    private void ApplyStoredLayout()
    {
        Columns.Deserialize(Settings.GetLayout(ColumnsKey));

        StorePaneColumn.Width = new GridLength(
            Settings.GetLayoutDouble(StorePaneKey, 230), GridUnitType.Pixel);
        MainGrid.SizeChanged += OnMainGridSizeChanged;

        // A height the user picked is restored as it was; a pane that was never sized by hand gets a
        // share of the window instead of a fixed number that could end up being a sliver.
        double stored = Settings.GetLayoutDouble(DetailsPaneKey, 0);
        _detailsHeight = stored >= MinimumDetailsHeight ? stored : 0;
        DetailsRow.Height = new GridLength(DetailsHeight(MainGrid.ActualHeight));

        _detailsVisible = Settings.GetLayoutBool(DetailsVisibleKey, true);
        ApplyDetailsVisibility();
    }

    /// <summary>
    /// The height of the details row: what the user picked, or - until they pick one - a share of the
    /// list area so the pane is never a sliver.
    /// </summary>
    private double DetailsHeight(double available)
    {
        if (_detailsHeight > 0)
        {
            return _detailsHeight;
        }

        return available <= 0 ? 300 : Math.Clamp(available * 0.45, 200, 480);
    }

    private void OnMainGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Shrinking the window must not push the details pane out of view, and a pane that only has a
        // computed height follows the window while it is being resized.
        double maximum = Math.Max(MinimumDetailsHeight, e.NewSize.Height - MinimumListHeight);
        double height = Math.Min(DetailsHeight(e.NewSize.Height), maximum);
        DetailsRow.Height = new GridLength(_detailsVisible ? height : 0);
    }

    private void PersistLayout()
    {
        Settings.SetLayout(ColumnsKey, Columns.Serialize());
        Settings.SetLayoutDouble(StorePaneKey, StorePaneColumn.ActualWidth);
        if (_detailsVisible && _detailsHeight > 0)
        {
            Settings.SetLayoutDouble(DetailsPaneKey, _detailsHeight);
        }
        Settings.SetLayoutBool(DetailsVisibleKey, _detailsVisible);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;

        ShowDetails(null);
        await ReloadAsync(selectStoreName: _storeName);
    }

    // ---------------------------------------------------------------- layout

    private void OnStoreSplitterDrag(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        double maximum = Math.Max(220, ContentGrid.ActualWidth - 320);
        StorePaneColumn.Width = new GridLength(
            Math.Clamp(StorePaneColumn.ActualWidth + e.Delta.Translation.X, 160, maximum),
            GridUnitType.Pixel);
        PersistLayout();
    }

    private void OnDetailsSplitterDrag(object sender, DragDeltaEventArgs e) => ResizeDetails(-e.VerticalChange);

    /// <summary>Arrow keys resize the pane as well, for keyboards. Up makes the details pane taller.</summary>
    private void OnDetailsSplitterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        double delta = e.Key switch
        {
            VirtualKey.Up => 24,
            VirtualKey.Down => -24,
            _ => 0,
        };

        if (delta == 0)
        {
            return;
        }

        ResizeDetails(delta);
        e.Handled = true;
    }

    /// <summary>Grows the details pane by <paramref name="delta"/> pixels, within the allowed range.</summary>
    private void ResizeDetails(double delta)
    {
        double maximum = Math.Max(MinimumDetailsHeight + 80, MainGrid.ActualHeight - MinimumListHeight);
        _detailsHeight = Math.Clamp(DetailsHeight(MainGrid.ActualHeight) + delta, MinimumDetailsHeight, maximum);
        DetailsRow.Height = new GridLength(_detailsHeight);
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

    private void OnTableSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Columns.FitTo(e.NewSize.Width - 20);
    }

    private void OnToggleDetailsClick(object sender, RoutedEventArgs e)
    {
        _detailsVisible = !_detailsVisible;
        ApplyDetailsVisibility();
        PersistLayout();
    }

    private void ApplyDetailsVisibility()
    {
        DetailsRow.Height = _detailsVisible
            ? new GridLength(DetailsHeight(MainGrid.ActualHeight))
            : new GridLength(0);

        DetailsPane.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;
        DetailsSplitter.Visibility = _detailsVisible ? Visibility.Visible : Visibility.Collapsed;

        DetailsToggleIcon.Glyph = _detailsVisible ? "\uE70D" : "\uE70E";
        DetailsToggleText.Text = Loc.Get(_detailsVisible ? "Stores_HideDetails" : "Stores_ShowDetails");
        ToolTipService.SetToolTip(DetailsToggleButton, Loc.Get(_detailsVisible ? "Stores_CollapseDetailsPane" : "Stores_ExpandDetailsPane"));
    }

    // ---------------------------------------------------------------- stores

    private async Task ReloadAsync(string selectStoreName)
    {
        BusyRing.IsActive = true;
        try
        {
            (List<StoreSummary> user, List<StoreSummary> machine) = await Task.Run(() =>
            {
                List<StoreSummary> u = _stores.GetStoreSummaries(StoreLocation.CurrentUser).ToList();
                List<StoreSummary> m = _stores.GetStoreSummaries(StoreLocation.LocalMachine).ToList();
                return (u, m);
            });

            _allUserStores = user;
            _allMachineStores = machine;
            ApplyStoreFilter();
            SelectStore(_location, selectStoreName);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Get("Stores_ErrorReadStores"), ex);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private void ApplyStoreFilter()
    {
        string query = StoreSearchBox?.Text?.Trim() ?? string.Empty;

        _userStores.Clear();
        foreach (StoreSummary summary in _allUserStores)
        {
            if (Matches(summary, query)) _userStores.Add(summary);
        }

        _machineStores.Clear();
        foreach (StoreSummary summary in _allMachineStores)
        {
            if (Matches(summary, query)) _machineStores.Add(summary);
        }

        if (StoreCountText is not null)
        {
            StoreCountText.Text = Loc.Format("Stores_StoreCount", _userStores.Count + _machineStores.Count);
        }

        UserStoreExpander.Visibility = _userStores.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        MachineStoreExpander.Visibility = _machineStores.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        static bool Matches(StoreSummary summary, string query)
            => query.Length == 0
               || summary.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || summary.Store.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnStoreSearchChanged(object sender, TextChangedEventArgs e) => ApplyStoreFilter();

    private void SelectStore(StoreLocation location, string storeName)
    {
        _suppressSelection = true;
        try
        {
            if (location == StoreLocation.CurrentUser)
            {
                MachineStoreList.SelectedItem = null;
                UserStoreList.SelectedItem = _userStores.FirstOrDefault(s => s.Store.Name == storeName) ?? _userStores.FirstOrDefault();
                if (UserStoreList.SelectedItem is StoreSummary picked)
                {
                    _location = picked.Store.Location;
                    _storeName = picked.Store.Name;
                }
            }
            else
            {
                UserStoreList.SelectedItem = null;
                MachineStoreList.SelectedItem = _machineStores.FirstOrDefault(s => s.Store.Name == storeName) ?? _machineStores.FirstOrDefault();
                if (MachineStoreList.SelectedItem is StoreSummary picked)
                {
                    _location = picked.Store.Location;
                    _storeName = picked.Store.Name;
                }
            }

            if (UserStoreList.SelectedItem is not null) UserStoreExpander.IsExpanded = true;
            if (MachineStoreList.SelectedItem is not null) MachineStoreExpander.IsExpanded = true;
        }
        finally
        {
            _suppressSelection = false;
        }

        UpdateStoreTitle();
        RefreshCertificates();
    }

    private void OnStoreSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || sender is not ListView list || list.SelectedItem is not StoreSummary summary)
        {
            return;
        }

        _suppressSelection = true;
        try
        {
            if (ReferenceEquals(list, UserStoreList)) MachineStoreList.SelectedItem = null;
            else UserStoreList.SelectedItem = null;
            if (ReferenceEquals(list, UserStoreList)) UserStoreExpander.IsExpanded = true;
            else MachineStoreExpander.IsExpanded = true;
        }
        finally
        {
            _suppressSelection = false;
        }

        _location = summary.Store.Location;
        _storeName = summary.Store.Name;
        UpdateStoreTitle();
        RefreshCertificates();
    }

    private void UpdateStoreTitle()
    {
        string locationText = Loc.Get(_location == StoreLocation.CurrentUser ? "Store_Location_CurrentUser" : "Store_Location_LocalMachine");
        StoreTitleText.Text = $"{locationText} \\ {CertificateStoreService.GetFriendlyStoreName(_storeName)}";
        ToolTipService.SetToolTip(StoreTitleText, $"{locationText}\\{_storeName}");
    }

    // ---------------------------------------------------------------- certificates

    private void RefreshCertificates()
    {
        IReadOnlyList<CertificateItem> items = _stores.GetCertificates(_location, _storeName, out string? error);
        _allCertificates = items.ToList();
        ApplyCertificateFilter();

        if (error is not null)
        {
            ShowErrorMessage(Loc.Format("Stores_ErrorReadStoreContents", error));
        }
        else
        {
            ClearStatus();
        }
    }

    private void ApplyCertificateFilter()
    {
        string query = SearchBox?.Text?.Trim() ?? string.Empty;

        IEnumerable<CertificateItem> filtered = _allCertificates;
        if (query.Length > 0)
        {
            filtered = _allCertificates.Where(c =>
                Contains(c.Subject, query) ||
                Contains(c.Issuer, query) ||
                Contains(c.Thumbprint, query) ||
                Contains(c.FriendlyName, query) ||
                Contains(c.CommonName, query));
        }

        _visibleCertificates.Clear();
        foreach (CertificateItem item in filtered)
        {
            _visibleCertificates.Add(new CertRow(item, Columns));
        }

        CertificateCountText.Text = _allCertificates.Count == _visibleCertificates.Count
            ? Loc.Format("Stores_CertCountAll", _visibleCertificates.Count)
            : Loc.Format("Stores_CertCountFiltered", _visibleCertificates.Count, _allCertificates.Count);

        CertEmptyState.Visibility = _visibleCertificates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CertEmptyText.Text = _allCertificates.Count == 0
            ? Loc.Get("Stores_EmptyCertificatesText")
            : Loc.Get("Stores_NoMatchingCertificates");

        static bool Contains(string? value, string query)
            => value is not null && value.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => ApplyCertificateFilter();

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await ReloadAsync(_storeName);

    private void OnCertificateSelectionChanged(object sender, SelectionChangedEventArgs e)
        => ShowDetails((CertList.SelectedItem as CertRow)?.Item);

    private void ShowDetails(CertificateItem? item)
    {
        if (item is null)
        {
            _currentSummary = null;
            DetailsPanel.Children.Clear();
            DetailsHeader.Text = Loc.Get("Stores_DetailsTitle");
            DetailsStatusText.Text = string.Empty;
            DetailsEmptyState.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            CertificateSummary summary = CertificateSummaryBuilder.Build(item.Certificate);
            _currentSummary = summary;
            DetailsHeader.Text = summary.DisplayName;
            DetailsStatusText.Text = string.Equals(summary.SubjectCommonName, summary.DisplayName, StringComparison.Ordinal)
                ? summary.ValidityText
                : summary.SubjectCommonName + " · " + summary.ValidityText;
            DetailPresenter.Render(DetailsPanel, DetailPresenter.ForCertificate(summary));
            DetailsEmptyState.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _currentSummary = null;
            DetailsPanel.Children.Clear();
            DetailsHeader.Text = item.DisplayName;
            DetailsStatusText.Text = Loc.Get("Stores_ErrorParseCertificate");
            DetailsEmptyState.Visibility = Visibility.Collapsed;
            DetailsPanel.Children.Add(new TextBlock
            {
                Text = DialogService.DescribeException(ex),
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    // ---------------------------------------------------------------- context menu

    private void OnCertificateRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not CertRow row)
        {
            return;
        }

        CertList.SelectedItem = row;

        var flyout = new MenuFlyout();
        flyout.Items.Add(BuildContextMenuItem(Loc.Get("Stores_CopyFingerprint"), "\uE8C8", () => CopyToClipboard(row.Item.Thumbprint)));
        flyout.Items.Add(BuildContextMenuItem(Loc.Get("Stores_CopyDetails"), "\uE8C8", CopyDetails));
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildContextMenuItem(Loc.Get("Stores_ExportCertificate"), "\uE898", () => _ = ExportAsync(row.Item)));
        flyout.Items.Add(BuildContextMenuItem(Loc.Get("Stores_OpenInCertMgr"), "\uE8A5", OpenCertMgr));
        flyout.Items.Add(BuildContextMenuItem(Loc.Get("Stores_DeleteCertificate"), "\uE74D", () => _ = DeleteAsync(row.Item)));

        flyout.ShowAt(element, new FlyoutShowOptions { Position = e.GetPosition(element) });
        e.Handled = true;
    }

    private static MenuFlyoutItem BuildContextMenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem
        {
            Text = text,
            Icon = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        item.Click += (_, _) => action();
        return item;
    }

    private void CopyDetails()
    {
        if (_currentSummary is null)
        {
            return;
        }

        CopyToClipboard(_currentSummary.RawTextReport);
    }

    // ---------------------------------------------------------------- actions

    private async void OnImportClick(object sender, RoutedEventArgs e) => await ImportAsync();

    private async Task ImportAsync()
    {
        try
        {
            string? path = await FilePickerHelper.PickOpenFileAsync(Loc.Get("Common_Import"), CertificateFileIOExtensions.All);
            if (path is null)
            {
                return;
            }

            byte[] data = await File.ReadAllBytesAsync(path);
            string? password = null;
            try
            {
                CertificateFileIO.Load(data, null);
            }
            catch (CryptographicException)
            {
                password = await DialogService.ShowPasswordAsync(Loc.Get("Stores_CertificatePassword"), Loc.Get("Stores_EnterPassword"));
                if (password is null)
                {
                    return;
                }
            }

            string locationText = Loc.Get(_location == StoreLocation.CurrentUser ? "Store_Location_CurrentUser" : "Store_Location_LocalMachine");
            bool confirmed = await DialogService.ShowConfirmAsync(
                Loc.Get("Stores_ImportTitle"),
                Loc.Format("Stores_ConfirmImport", Path.GetFileName(path), $"{locationText}\\{CertificateStoreService.GetFriendlyStoreName(_storeName)}"),
                Loc.Get("Common_Import"));
            if (!confirmed)
            {
                return;
            }

            _stores.ImportCertificateToStore(data, password, _location, _storeName);
            await ReloadAsync(_storeName);
            await DialogService.ShowMessageAsync(Loc.Get("Stores_ImportSuccessTitle"), Loc.Get("Stores_ImportSuccessMessage"));
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_ImportFailed"), ex);
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
        => await ExportAsync((CertList.SelectedItem as CertRow)?.Item);

    private async Task ExportAsync(CertificateItem? item)
    {
        if (item is null)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportTitle"), Loc.Get("Stores_SelectCertificateFirst"));
            return;
        }

        try
        {
            var formatBox = new ComboBox
            {
                Header = Loc.Get("X509_ExportFormat"),
                ItemsSource = new[]
                {
                    Loc.Get("Stores_ExportDerCert"),
                    Loc.Get("Stores_ExportPemCert"),
                    Loc.Get("Stores_ExportPfx"),
                },
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            var includeKey = new CheckBox
            {
                Content = Loc.Get("X509_ExportIncludePrivateKey"),
                IsEnabled = false,
            };
            var passwordBox = new PasswordBox { Header = Loc.Get("Stores_PfxPassword"), IsEnabled = false };

            formatBox.SelectionChanged += (_, _) =>
            {
                bool isPfx = formatBox.SelectedIndex == 2;
                includeKey.IsEnabled = isPfx && item.HasPrivateKey;
                if (!isPfx)
                {
                    includeKey.IsChecked = false;
                }
                passwordBox.IsEnabled = isPfx;
            };

            var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
            panel.Children.Add(new TextBlock
            {
                Text = item.DisplayName,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(formatBox);
            panel.Children.Add(includeKey);
            panel.Children.Add(passwordBox);

            var dialog = new ContentDialog
            {
                Title = Loc.Get("X509_ExportOptionsTitle"),
                Content = panel,
                PrimaryButtonText = Loc.Get("Common_Export"),
                CloseButtonText = Loc.Get("Common_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };

            if (await DialogService.ShowAsync(dialog) != ContentDialogResult.Primary)
            {
                return;
            }

            int format = formatBox.SelectedIndex;
            bool withPrivateKey = includeKey.IsChecked == true;
            string? password = string.IsNullOrEmpty(passwordBox.Password) ? null : passwordBox.Password;

            (string name, string[] extensions)[] types = format switch
            {
                0 => new[] { (Loc.Get("Stores_SaveDerCert"), new[] { ".cer" }) },
                1 => new[] { (Loc.Get("Stores_SavePemCert"), new[] { ".crt", ".pem" }) },
                _ => new[] { ("PKCS#12", new[] { ".pfx", ".p12" }) },
            };

            string? savePath = await FilePickerHelper.PickSaveFileAsync(SanitizeFileName(item.DisplayName), Loc.Get("Common_Export"), types);
            if (savePath is null)
            {
                return;
            }

            CertificateFileFormat fileFormat = format switch
            {
                0 => CertificateFileFormat.Der,
                1 => CertificateFileFormat.Pem,
                _ => CertificateFileFormat.Pkcs12,
            };

            CertificateFileIO.Export(item.Certificate, savePath, fileFormat, password, withPrivateKey);
            await DialogService.ShowMessageAsync(Loc.Get("Stores_ExportSuccessTitle"), Loc.Get("Common_ExportedTo") + savePath);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_ExportFailed"), ex);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
        => await DeleteAsync((CertList.SelectedItem as CertRow)?.Item);

    private async Task DeleteAsync(CertificateItem? item)
    {
        if (item is null)
        {
            await DialogService.ShowMessageAsync(Loc.Get("Stores_DeleteTitle"), Loc.Get("Stores_SelectCertificateFirst"));
            return;
        }

        bool confirmed = await DialogService.ShowConfirmAsync(
            Loc.Get("Stores_DeleteTitle"),
            Loc.Format("Stores_ConfirmDelete", item.DisplayName, item.Subject, item.Thumbprint),
            Loc.Get("Common_Delete"));
        if (!confirmed)
        {
            return;
        }

        try
        {
            _stores.RemoveCertificate(_location, _storeName, item.Certificate);
            await ReloadAsync(_storeName);
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Stores_DeleteFailed"), ex);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (CertList.SelectedItem is not CertRow row)
        {
            _ = DialogService.ShowMessageAsync(Loc.Get("Stores_CopyFingerprint"), Loc.Get("Stores_SelectCertificateFirst"));
            return;
        }

        CopyToClipboard(row.Item.Thumbprint);
    }

    private void OnCopyDetailsClick(object sender, RoutedEventArgs e) => CopyDetails();

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnOpenCertMgrClick(object sender, RoutedEventArgs e) => OpenCertMgr();

    private void OpenCertMgr()
    {
        try
        {
            Process.Start(new ProcessStartInfo("certmgr.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ = DialogService.ShowErrorAsync(Loc.Get("Error_OpenCertMgrFailed"), ex);
        }
    }

    private async void OnElevateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (string.IsNullOrEmpty(executable))
            {
                return;
            }

            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            await DialogService.ShowErrorAsync(Loc.Get("Error_RestartElevatedFailed"), ex);
        }
    }

    private void ShowError(string title, Exception exception)
    {
        AppServices.Log(title + ": " + exception);
        ShowErrorMessage(title + Loc.Get("Common_DetailsSuffix") + DialogService.DescribeException(exception));
    }

    private void ShowErrorMessage(string message)
    {
        StatusBar.Severity = InfoBarSeverity.Error;
        StatusBar.Title = Loc.Get("Common_Error");
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private void ClearStatus() => StatusBar.IsOpen = false;

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "certificate";
        }
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }
        return name;
    }
}

public static class CertificateFileIOExtensions
{
    public static string[] All => new[] { ".cer", ".crt", ".der", ".pem", ".pfx", ".p12", ".p7b", ".sst" };
}
