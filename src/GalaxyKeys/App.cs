using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace GalaxyKeys;

public sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        var app = new App();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Theme.xaml") });
        app.DispatcherUnhandledException += (_, e) => { UI.Message(app.MainWindow, "The operation could not be completed. Your saved vault has not been reset.", "Galaxy Keys"); e.Handled = true; };
        app.Run(new MainWindow());
    }
}

public static class UI
{
    public static readonly Brush Bg = Brush("#0E111A"), Panel = Brush("#171C29"), Muted = Brush("#A4ADC1"), Text = Brush("#F4F3ED");
    public static Brush Gold => (Brush)Application.Current.Resources["GalaxyGold"];
    public static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public static TextBlock Label(string value, double size = 14, Brush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    public static System.Windows.Controls.Image Logo(double size) => new() { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/galaxy-keys-logo.png")), Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4), Background = primary ? Gold : Brush("#202737"), Foreground = primary ? Bg : Text, HorizontalContentAlignment = HorizontalAlignment.Center };
        b.Click += click; return b;
    }
    public static TextBox Input(string value = "") => new() { Text = value, FontSize = 15, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) };
    public static PasswordBox Password() => new() { FontSize = 15, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16) };
    public static Window Dialog(Window owner, string title, double width = 470)
    {
        var window = new Window { Owner = owner, Title = title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, Background = Panel, Foreground = Text, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, MaxHeight = 850 };
        ConfigureWindow(window); return window;
    }
    public static void ConfigureWindow(Window window)
    {
        window.Style = (Style)Application.Current.Resources["GalaxyWindow"];
        window.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/galaxy-keys.ico"));
        System.Windows.Shell.WindowChrome.SetWindowChrome(window, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 48, CornerRadius = new CornerRadius(20), GlassFrameThickness = new Thickness(0),
            ResizeBorderThickness = new Thickness(window.ResizeMode == ResizeMode.NoResize ? 0 : 6), UseAeroCaptionButtons = false
        });
        window.SourceInitialized += (_, _) =>
        {
            try { var dark = 1; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 20, ref dark, sizeof(int)); var round = 2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref round, sizeof(int)); } catch { }
        };
        window.Loaded += (_, _) =>
        {
            if (window.Template.FindName("GalaxyClose", window) is Button close) close.Click += (_, _) => window.Close();
            if (window.Template.FindName("GalaxyMinimize", window) is Button minimize) minimize.Click += (_, _) => SystemCommands.MinimizeWindow(window);
            if (window.Template.FindName("GalaxyMaximize", window) is Button maximize) maximize.Click += (_, _) => { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); };
        };
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void Error(Window owner, string text) => Message(owner, text, "Galaxy Keys", MessageBoxButton.OK, MessageBoxImage.Warning);
    public static MessageBoxResult Message(Window owner, string text, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = Dialog(owner, title); var panel = new StackPanel { Margin = new Thickness(28) }; dialog.Content = panel;
        panel.Children.Add(Logo(54)); panel.Children.Add(Label(text, 15));
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; var result = MessageBoxResult.Cancel;
        if (buttons == MessageBoxButton.YesNo)
        {
            row.Children.Add(Button("Yes", (_, _) => { result = MessageBoxResult.Yes; dialog.Close(); }, true));
            row.Children.Add(Button("Cancel", (_, _) => { result = MessageBoxResult.No; dialog.Close(); }));
        }
        else row.Children.Add(Button("OK", (_, _) => { result = MessageBoxResult.OK; dialog.Close(); }, true));
        panel.Children.Add(row); dialog.ShowDialog(); return result;
    }
}

public sealed class MainWindow : Window
{
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GalaxyKeys");
    readonly Vault vault;
    FileStream? exclusive;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    DateTime lastActivity = DateTime.UtcNow;
    DateTime retryAfter;
    int failures;
    string filter = "All items";
    readonly Dictionary<string, Button> navigation = [];
    TextBox? search;
    ListBox? list;
    StackPanel? detail;
    TextBlock? status;
    Entry? selected;
    Window? activeDialog;
    bool busy;
    long session;
    string? copied;
    DateTime clipboardUntil;
    IntPtr Hwnd => new WindowInteropHelper(this).Handle;

    public MainWindow()
    {
        Title = "Galaxy Keys"; UI.ConfigureWindow(this); Width = 1180; Height = 800; MinWidth = 900; MinHeight = 620;
        Background = UI.Bg; Foreground = UI.Text; FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        vault = new Vault(Path.Combine(folder, "vault.gkv"));
        PreviewKeyDown += (_, _) => lastActivity = DateTime.UtcNow;
        PreviewMouseDown += (_, _) => lastActivity = DateTime.UtcNow;
        PreviewMouseMove += (_, _) => lastActivity = DateTime.UtcNow;
        SourceInitialized += (_, _) => HwndSource.FromHwnd(Hwnd)?.AddHook(WindowMessage);
        Loaded += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(folder);
                // A lifetime file handle prevents two processes from overwriting the same vault.
                exclusive = new FileStream(Path.Combine(folder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                Native.WTSRegisterSessionNotification(Hwnd, 0);
                ShowLocked(); timer.Start();
            }
            catch { UI.Error(this, "Galaxy Keys is already open, or its local data folder is unavailable."); Close(); }
        };
        timer.Tick += (_, _) =>
        {
            if (copied != null && DateTime.UtcNow >= clipboardUntil) ClearClipboard();
            if (vault.IsOpen && !busy && DateTime.UtcNow - lastActivity > TimeSpan.FromMinutes(5)) Lock();
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && vault.IsOpen && !busy) Lock(); };
        Closed += (_, _) => { timer.Stop(); ClearClipboard(); vault.Dispose(); exclusive?.Dispose(); Native.WTSUnRegisterSessionNotification(Hwnd); };
    }
    IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if ((msg == 0x02B1 && wparam.ToInt32() == 7) || (msg == 0x0218 && wparam.ToInt32() == 4)) Lock();
        return IntPtr.Zero;
    }
    public void Lock()
    {
        session++; if (activeDialog != null) { activeDialog.Close(); activeDialog = null; }
        vault.Lock(); selected = null; ClearClipboard(); search = null; list = null; detail = null; ShowLocked();
    }
    void ClearClipboard()
    {
        try { if (copied != null && Clipboard.ContainsText() && Clipboard.GetText() == copied) Clipboard.Clear(); } catch { }
        copied = null;
    }
    void Copy(string value)
    {
        try
        {
            var data = new DataObject(); data.SetText(value);
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
            data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
            Clipboard.SetDataObject(data, false); copied = value; clipboardUntil = DateTime.UtcNow.AddSeconds(20);
            if (status != null) status.Text = "Copied • clipboard clears in 20 seconds";
        }
        catch { UI.Error(this, "Clipboard is busy. Please try again."); }
    }
    void ShowLocked()
    {
        var create = !File.Exists(vault.Path);
        var root = new Grid { Margin = new Thickness(28) };
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var hero = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(40, 0, 24, 0) };
        hero.Children.Add(UI.Logo(240)); hero.Children.Add(UI.Label("Galaxy Keys", 38, UI.Gold));
        hero.Children.Add(UI.Label("Your keys. Your universe.", 19)); hero.Children.Add(UI.Label("A private space for your API keys\nand website logins.", 14, UI.Muted));
        root.Children.Add(hero);
        var card = new StackPanel { MaxWidth = 410, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 24, 28, 24) }; Grid.SetColumn(card, 1);
        card.Children.Add(UI.Label("YOUR LOCAL VAULT", 12, UI.Gold));
        card.Children.Add(UI.Label(create ? "Your secrets.\nYour computer." : "Welcome back.", 34));
        card.Children.Add(UI.Label(create ? "Create your private, encrypted vault. No accounts. No cloud." : "Unlock your local vault to access your keys and logins.", 15, UI.Muted));
        card.Children.Add(UI.Label("Master password")); var password = UI.Password(); card.Children.Add(password);
        PasswordBox? confirm = null;
        if (create) { card.Children.Add(UI.Label("Confirm master password")); confirm = UI.Password(); card.Children.Add(confirm); card.Children.Add(UI.Label("At least 14 characters. Keep this password safe: there is no password reset.", 12, UI.Muted)); }
        var message = UI.Label("", 13, UI.Gold); var buttons = new StackPanel();
        async void UnlockPassword(object? sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (DateTime.UtcNow < retryAfter) { message.Text = "Please wait before trying again."; return; }
            var pass = password.Password; password.Clear(); var match = confirm?.Password; confirm?.Clear();
            if (create && pass != match) { message.Text = "Passwords do not match."; return; }
            busy = true; buttons.IsEnabled = false; message.Text = create ? "Creating encrypted vault…" : "Unlocking…"; var stamp = session;
            try
            {
                await Task.Run(() => { if (create) vault.Create(pass); else vault.Unlock(pass); });
                if (session != stamp) { vault.Lock(); return; }
                failures = 0; lastActivity = DateTime.UtcNow; ShowVault();
            }
            catch (Exception ex)
            {
                failures++; retryAfter = DateTime.UtcNow.AddSeconds(Math.Min(30, failures * 2));
                message.Text = ex is ArgumentException ? ex.Message : "Unable to unlock. Check your password. If the vault is damaged, restore an encrypted backup.";
            }
            finally { pass = ""; busy = false; buttons.IsEnabled = true; }
        }
        buttons.Children.Add(UI.Button(create ? "Create local vault" : "Unlock vault", UnlockPassword, true));
        password.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) UnlockPassword(s, e); };
        if (confirm != null) confirm.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) UnlockPassword(s, e); };
        if (!create && Hello.IsEnabled(vault.Path)) buttons.Children.Add(UI.Button("Unlock with Windows Hello", (_, _) =>
        {
            if (busy) return;
            try { busy = true; var stamp = session; Hello.Unlock(vault, Hwnd); if (stamp != session) { vault.Lock(); return; } lastActivity = DateTime.UtcNow; ShowVault(); }
            catch { message.Text = "Windows Hello was canceled or is unavailable. Use your master password."; }
            finally { busy = false; }
        }));
        card.Children.Add(buttons); card.Children.Add(message); card.Children.Add(UI.Label("LOCAL ONLY  •  AES-256-GCM ENCRYPTION", 11, UI.Muted));
        root.Children.Add(card); Content = root; password.Focus();
    }
    void ShowVault()
    {
        navigation.Clear();
        var root = new Grid { Margin = new Thickness(14) }; root.ColumnDefinitions.Add(new() { Width = new GridLength(210) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(320) }); root.ColumnDefinitions.Add(new());
        var sidebar = new DockPanel { Background = UI.Panel, Margin = new Thickness(0, 0, 1, 0) }; Grid.SetColumn(sidebar, 0);
        var foot = new StackPanel { Margin = new Thickness(18) }; DockPanel.SetDock(foot, Dock.Bottom);
        foot.Children.Add(UI.Label("●  Local vault unlocked", 12, UI.Gold));
        foot.Children.Add(UI.Button("Settings & backup", (_, _) => Settings())); foot.Children.Add(UI.Button("Lock vault", (_, _) => Lock())); sidebar.Children.Add(foot);
        var nav = new StackPanel { Margin = new Thickness(18, 28, 12, 0) }; nav.Children.Add(UI.Logo(72)); nav.Children.Add(UI.Label("Galaxy Keys", 22, UI.Gold)); nav.Children.Add(UI.Label("PRIVATE BY DESIGN", 10, UI.Muted));
        foreach (var (label, kind) in new[] { ("All items", "All items"), ("API keys", "API Key"), ("Website logins", "Login") }) { var button = UI.Button(label, (_, _) => { filter = kind; Refresh(); }); navigation[kind] = button; nav.Children.Add(button); }
        nav.Children.Add(UI.Label("\nEverything stays on\nthis computer.", 12, UI.Muted)); sidebar.Children.Add(nav); sidebar.Background = Brushes.Transparent;
        var sidebarCard = new Border { CornerRadius = new CornerRadius(20), Background = UI.Panel, Child = sidebar }; Grid.SetColumn(sidebarCard, 0); root.Children.Add(sidebarCard);
        var middle = new DockPanel { Margin = new Thickness(22, 28, 20, 20) }; Grid.SetColumn(middle, 1);
        var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); top.Children.Add(UI.Label("Your vault", 26));
        top.Children.Add(UI.Label("Search name or username", 12, UI.Muted)); search = UI.Input(); search.TextChanged += (_, _) => Refresh(); top.Children.Add(search);
        top.Children.Add(UI.Button("+  Add item", (_, _) => Edit(null), true)); middle.Children.Add(top);
        status = UI.Label("", 11, UI.Muted); DockPanel.SetDock(status, Dock.Bottom); middle.Children.Add(status);
        list = new ListBox { Background = Brushes.Transparent, Foreground = UI.Text, BorderThickness = new Thickness(0), Margin = new Thickness(0, 18, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        list.SelectionChanged += (_, _) => { selected = (list.SelectedItem as ListBoxItem)?.Tag as Entry; ShowDetail(); }; middle.Children.Add(list); root.Children.Add(middle);
        detail = new StackPanel { Margin = new Thickness(30, 40, 30, 20) };
        var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(2) };
        var detailCard = new Border { CornerRadius = new CornerRadius(20), Background = UI.Panel, Child = scroll }; Grid.SetColumn(detailCard, 2); root.Children.Add(detailCard);
        Content = root; Refresh();
    }
    void Refresh()
    {
        if (list == null || !vault.IsOpen) return;
        foreach (var (kind, button) in navigation)
        {
            button.Background = kind == filter ? (Brush)Application.Current.Resources["GalaxyGoldSoft"] : UI.Brush("#202737");
            button.BorderBrush = kind == filter ? UI.Gold : UI.Brush("#343D51");
            button.Foreground = kind == filter ? UI.Gold : UI.Text;
        }
        var id = selected?.Id; list.Items.Clear();
        foreach (var item in vault.Search(search?.Text.Trim() ?? "", filter))
        {
            var content = new StackPanel { Margin = new Thickness(8) };
            content.Children.Add(UI.Label(item.Name, 15)); content.Children.Add(UI.Label(item.Kind == "Login" ? item.Username : "API Key", 12, UI.Muted));
            var row = new ListBoxItem { Content = content, Tag = item, Foreground = UI.Text, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6) }; list.Items.Add(row);
            if (item.Id == id) list.SelectedItem = row;
        }
        status!.Text = $"{list.Items.Count} items • {filter}";
        if (list.SelectedItem == null) { selected = null; ShowDetail(); }
    }
    void ShowDetail()
    {
        if (detail == null) return; detail.Children.Clear();
        if (selected == null) { detail.Children.Add(UI.Logo(100)); detail.Children.Add(UI.Label("A safe place for\nyour digital keys.", 32)); detail.Children.Add(UI.Label("Select an item or add your first API key or website login.", 15, UI.Muted)); return; }
        var item = selected;
        detail.Children.Add(UI.Label(item.Kind.ToUpperInvariant(), 11, UI.Gold)); detail.Children.Add(UI.Label(item.Name, 30));
        void Field(string label, string value)
        {
            detail.Children.Add(UI.Label(label, 12, UI.Muted)); var t = UI.Input(value); t.IsReadOnly = true; detail.Children.Add(t);
        }
        if (item.Kind == "Login") { Field("Username", item.Username); detail.Children.Add(UI.Button("Copy username", (_, _) => Copy(item.Username))); }
        if (!string.IsNullOrWhiteSpace(item.Website)) Field("Website", item.Website);
        detail.Children.Add(UI.Label(item.Kind == "Login" ? "Password" : "API key", 12, UI.Muted));
        var masked = UI.Password(); masked.Password = item.Secret; masked.IsEnabled = false; detail.Children.Add(masked);
        var revealed = UI.Input(); revealed.IsReadOnly = true; revealed.Visibility = Visibility.Collapsed; detail.Children.Add(revealed);
        var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        void Hide() { revealed.Clear(); revealed.Visibility = Visibility.Collapsed; masked.Visibility = Visibility.Visible; hideTimer.Stop(); }
        hideTimer.Tick += (_, _) => Hide(); revealed.Unloaded += (_, _) => Hide();
        var actions = new WrapPanel(); actions.Children.Add(UI.Button("Copy secret", (_, _) => Copy(item.Secret), true));
        actions.Children.Add(UI.Button("Show / hide", (_, _) => { if (revealed.Visibility == Visibility.Visible) Hide(); else { revealed.Text = item.Secret; revealed.Visibility = Visibility.Visible; masked.Visibility = Visibility.Collapsed; hideTimer.Start(); } })); detail.Children.Add(actions);
        detail.Children.Add(UI.Label("\nUpdated " + item.Updated.ToLocalTime().ToString("g"), 11, UI.Muted));
        var manage = new WrapPanel(); manage.Children.Add(UI.Button("Edit item", (_, _) => Edit(item))); manage.Children.Add(UI.Button("Delete", (_, _) =>
        {
            if (UI.Message(this, $"Delete ‘{item.Name}’?", "Delete item", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { vault.Delete(item.Id); selected = null; Refresh(); } catch { UI.Error(this, "Could not save the change. The item has been kept."); }
        })); detail.Children.Add(manage);
    }
    void Edit(Entry? entry)
    {
        var dialog = UI.Dialog(this, entry == null ? "Add item" : "Edit item", 510); activeDialog = dialog;
        var p = new StackPanel { Margin = new Thickness(28) }; dialog.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        p.Children.Add(UI.Label(entry == null ? "Add to your vault" : "Edit item", 26)); p.Children.Add(UI.Label("What would you like to store?", 12, UI.Muted));
        var kind = new ComboBox { ItemsSource = new[] { "API Key", "Login" }, SelectedItem = entry?.Kind ?? "API Key", Margin = new Thickness(0, 0, 0, 20), Padding = new Thickness(10), FontSize = 15 }; p.Children.Add(kind);
        p.Children.Add(UI.Label("Name")); var name = UI.Input(entry?.Name ?? ""); p.Children.Add(name);
        var login = new StackPanel(); login.Children.Add(UI.Label("Username / email")); var username = UI.Input(entry?.Username ?? ""); login.Children.Add(username); login.Children.Add(UI.Label("Website (optional)")); var website = UI.Input(entry?.Website ?? ""); login.Children.Add(website); p.Children.Add(login);
        var secretLabel = UI.Label("API key"); p.Children.Add(secretLabel); var secret = UI.Password(); secret.Password = entry?.Secret ?? ""; p.Children.Add(secret);
        var generate = UI.Button("Generate strong password", (_, _) => { secret.Password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)); }); p.Children.Add(generate);
        void UpdateKind() { var isLogin = (string)kind.SelectedItem == "Login"; login.Visibility = generate.Visibility = isLogin ? Visibility.Visible : Visibility.Collapsed; secretLabel.Text = isLogin ? "Password" : "API key"; }
        kind.SelectionChanged += (_, _) => UpdateKind(); UpdateKind(); var error = UI.Label("", 12, UI.Gold); p.Children.Add(error);
        var row = new WrapPanel(); row.Children.Add(UI.Button("Save item", (_, _) =>
        {
            if (!vault.IsOpen) { dialog.Close(); return; }
            if (string.IsNullOrWhiteSpace(name.Text) || secret.Password.Length == 0 || ((string)kind.SelectedItem == "Login" && string.IsNullOrWhiteSpace(username.Text))) { error.Text = "Enter a name, secret, and a username for logins."; return; }
            if (name.Text.Length > 200 || username.Text.Length > 1000 || website.Text.Length > 2000 || secret.Password.Length > 65536) { error.Text = "One of the fields is too long."; return; }
            var isLogin = (string)kind.SelectedItem == "Login";
            var item = new Entry(entry?.Id ?? Guid.NewGuid().ToString("N"), (string)kind.SelectedItem, name.Text.Trim(), isLogin ? username.Text.Trim() : "", secret.Password, isLogin ? website.Text.Trim() : "", DateTimeOffset.UtcNow);
            try { vault.Put(item); selected = item; dialog.Close(); Refresh(); } catch { error.Text = "Unable to save. Your previous vault is unchanged."; }
        }, true)); row.Children.Add(UI.Button("Cancel", (_, _) => dialog.Close())); p.Children.Add(row);
        dialog.PreviewKeyDown += (_, _) => lastActivity = DateTime.UtcNow; dialog.PreviewMouseDown += (_, _) => lastActivity = DateTime.UtcNow;
        dialog.Closed += (_, _) => { secret.Clear(); username.Clear(); website.Clear(); name.Clear(); activeDialog = null; }; dialog.ShowDialog();
    }
    void Settings()
    {
        var dialog = UI.Dialog(this, "Settings & backup", 530); activeDialog = dialog; var p = new StackPanel { Margin = new Thickness(28) }; dialog.Content = p;
        p.Children.Add(UI.Label("Your local vault", 26)); p.Children.Add(UI.Label("Auto-lock: 5 minutes of inactivity, minimize, Windows lock, or sleep. Clipboard: 20 seconds.", 13, UI.Muted));
        p.Children.Add(UI.Label("Windows Hello", 18));
        p.Children.Add(UI.Label("Optional PIN, fingerprint, or face unlock on this PC. Enrollment must successfully unlock a cryptographically protected key. Your master password always remains available.", 13, UI.Muted));
        var hello = UI.Button(Hello.IsEnabled(vault.Path) ? "Disable Windows Hello" : "Enable Windows Hello", (_, _) => { });
        hello.Click += (_, _) =>
        {
            try
            {
                busy = true;
                if (Hello.IsEnabled(vault.Path)) Hello.Disable(vault.Path); else Hello.Enable(vault, new WindowInteropHelper(dialog).Handle);
                hello.Content = Hello.IsEnabled(vault.Path) ? "Disable Windows Hello" : "Enable Windows Hello";
            }
            catch { UI.Error(dialog, "Windows Hello could not complete this operation. Check Windows Settings > Accounts > Sign-in options. No weaker unlock method will be substituted."); }
            finally { busy = false; lastActivity = DateTime.UtcNow; }
        }; p.Children.Add(hello);
        p.Children.Add(UI.Button("Change master password", (_, _) => { dialog.Close(); ChangePassword(); }));
        p.Children.Add(UI.Label("Encrypted backup", 18)); p.Children.Add(UI.Label("Backups require the master password used when they were created. Keep a copy on an external drive. Windows Hello enrollment is not exported.", 13, UI.Muted));
        p.Children.Add(UI.Button("Save encrypted backup…", (_, _) =>
        {
            var picker = new SaveFileDialog { FileName = "GalaxyKeys-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".gkv", Filter = "Encrypted Galaxy Keys vault|*.gkv", OverwritePrompt = true };
            if (picker.ShowDialog(dialog) != true) return;
            try
            {
                if (Path.GetFullPath(picker.FileName).StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException();
                Vault.AtomicWrite(picker.FileName, File.ReadAllBytes(vault.Path), false); UI.Message(dialog, "Encrypted backup saved.", "Galaxy Keys");
            }
            catch { UI.Error(dialog, "Could not save backup. Choose a location outside the Galaxy Keys data folder."); }
        }));
        p.Children.Add(UI.Label("Data folder", 12, UI.Muted)); var location = UI.Input(folder); location.IsReadOnly = true; p.Children.Add(location);
        p.Children.Add(UI.Label("Restore: close Galaxy Keys, keep a copy of the current vault.gkv, then replace it with your backup. Unlock with that backup’s master password. The .bak file is the previous encrypted save.", 12, UI.Muted));
        p.Children.Add(UI.Button("Close", (_, _) => dialog.Close())); dialog.Closed += (_, _) => activeDialog = null; dialog.ShowDialog();
    }
    void ChangePassword()
    {
        var dialog = UI.Dialog(this, "Change master password"); activeDialog = dialog; var p = new StackPanel { Margin = new Thickness(28) }; dialog.Content = p;
        p.Children.Add(UI.Label("Change master password", 24)); p.Children.Add(UI.Label("Current password")); var old = UI.Password(); p.Children.Add(old);
        p.Children.Add(UI.Label("New password (14+ characters)")); var next = UI.Password(); p.Children.Add(next); p.Children.Add(UI.Label("Confirm new password")); var confirm = UI.Password(); p.Children.Add(confirm);
        var message = UI.Label("", 12, UI.Gold); p.Children.Add(message);
        p.Children.Add(UI.Button("Change password", (_, _) =>
        {
            if (!vault.IsOpen) { dialog.Close(); return; }
            if (next.Password != confirm.Password) { message.Text = "Passwords do not match."; return; }
            try { vault.ChangePassword(old.Password, next.Password); dialog.Close(); UI.Message(this, "Password changed. Existing backups still use their original password.", "Galaxy Keys"); }
            catch (Exception ex) { message.Text = ex is ArgumentException ? ex.Message : "Unable to change password. Check your current password."; }
        }, true)); p.Children.Add(UI.Button("Cancel", (_, _) => dialog.Close()));
        dialog.Closed += (_, _) => { old.Clear(); next.Clear(); confirm.Clear(); activeDialog = null; }; dialog.ShowDialog();
    }
    static class Native
    {
        [System.Runtime.InteropServices.DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, int flags);
        [System.Runtime.InteropServices.DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
    }
}
