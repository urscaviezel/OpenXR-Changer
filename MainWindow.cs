using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OpenXRChanger;
public sealed class MainWindow : Window
{
    readonly Preferences prefs;
    readonly Language l = new();
    System.Windows.Controls.Primitives.UniformGrid cards = new(); TextBlock status = new();
    string active = "";
    bool checking;
    public MainWindow()
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "OpenXR Changer · v" + Updater.Version; Width = 720; MinWidth = 580; MinHeight = 150;
        SizeToContent = SizeToContent.Height;
        MaxHeight = Math.Max(150, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        prefs = Store.Load(); l.Load(prefs.Language);
        RuntimeRegistry.Discover(prefs); Store.Save(prefs); Build();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) => { if (RuntimeRegistry.Active != active) Refresh(); }; timer.Start();
        Closed += (_, _) => timer.Stop();
        Loaded += async (_, _) => { if (prefs.CheckUpdates) await CheckUpdates(false); };
    }
    static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    public static TextBlock Text(string text, double size = 14, string color = "#dce1f0") => new() { Text = text, FontSize = size, Foreground = Brush(color), Margin = new Thickness(0, 0, 0, 6) };
    public static Button Button(string text, Action action) { var button = new Button { Content = text }; button.Click += (_, _) => action(); return button; }
    static Border Card(UIElement child, string color = "#0e1530") => new() { Background = Brush(color), BorderBrush = Brush("#253056"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 12), Child = child };
    void Build()
    {
        l.Load(prefs.Language);
        var root = new DockPanel(); Content = root;
        var menu = new Menu(); DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        var info = new MenuItem { Header = "Info" }; menu.Items.Add(info);
        void Item(MenuItem parent, string name, Action action) { var item = new MenuItem { Header = name }; item.Click += (_, _) => action(); parent.Items.Add(item); }
        Item(info, l["about"], About);
        Item(info, l["checkUpdates"], async () => await CheckUpdates(true));
        var settings = new MenuItem { Header = l["settings"] }; menu.Items.Add(settings);
        Item(settings, l["languageSettings"], Settings); Item(settings, l["manage"], Manage);
        var autoUpdates = new MenuItem { Header = l["autoUpdates"], IsCheckable = true, IsChecked = prefs.CheckUpdates, StaysOpenOnClick = true };
        autoUpdates.Click += (_, _) => Safe(() => { var previous = prefs.CheckUpdates; prefs.CheckUpdates = autoUpdates.IsChecked; try { Store.Save(prefs); } catch { prefs.CheckUpdates = previous; autoUpdates.IsChecked = previous; throw; } });
        settings.Items.Add(autoUpdates);
        var footer = new DockPanel { Margin = new Thickness(16, 4, 16, 8) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        status = Text(l["ready"], 11, "#8e98b8"); status.Margin = new Thickness(0); status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; footer.Children.Add(status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll);
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 0) }; scroll.Content = body;
        var toolbar = new DockPanel { Margin = new Thickness(4, 0, 0, 10) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right);
        actions.Children.Add(Button(l["refresh"], () => Safe(() => { RuntimeRegistry.Discover(prefs); Store.Save(prefs); Refresh(); })));
        actions.Children.Add(Button(l["manageShort"], Manage));
        foreach (Button button in actions.Children) { button.Padding = new Thickness(10, 6, 10, 6); button.FontSize = 12; }
        toolbar.Children.Add(actions);
        var label = Text(l["runtimes"], 17, "#ffffff"); label.Margin = new Thickness(0); label.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(label); body.Children.Add(toolbar);
        cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, VerticalAlignment = VerticalAlignment.Top }; body.Children.Add(cards);
        Refresh();
    }
    void Refresh()
    {
        active = RuntimeRegistry.Active;
        cards.Children.Clear();
        foreach (var r in prefs.Runtimes.Where(r => r.Visible).OrderByDescending(r => RuntimeRegistry.Same(r.ResolvedPath, active)).ThenBy(r => r.Name))
        {
            bool isActive = RuntimeRegistry.Same(r.ResolvedPath, active), installed = r.Installed;
            var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(50) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            FrameworkElement icon = Text(r.Name.StartsWith("Virtual") ? "VD" : r.Name[..Math.Min(2, r.Name.Length)].ToUpperInvariant(), 22, "#9b5cff");
            icon = RuntimeLogo.Create(r) ?? icon;
            icon.VerticalAlignment = VerticalAlignment.Center; icon.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(icon);
            var name = Text(r.Name, 13, "#ffffff"); name.Margin = new Thickness(0, 0, 8, 0); name.VerticalAlignment = VerticalAlignment.Center; name.MaxHeight = 40; name.TextTrimming = TextTrimming.CharacterEllipsis; name.ToolTip = r.Name; Grid.SetColumn(name, 1); row.Children.Add(name);
            FrameworkElement action;
            if (isActive && installed)
            {
                action = new Border { Background = Brush("#6534ad"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 6, 12, 6), Child = new TextBlock { Text = "● " + l["active"], Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.NoWrap } };
            }
            else
            {
                var activate = Button(installed ? l["activate"] : l["missing"], async () => await Activate(r)); activate.IsEnabled = installed && !isActive; activate.Margin = new Thickness(0); activate.FontSize = 12; activate.Padding = new Thickness(10, 6, 10, 6); activate.ToolTip = isActive ? l["invalidActive"] : l["switchHint"]; action = activate;
            }
            action.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(action, 2); row.Children.Add(action);
            cards.Children.Add(new Border { Child = row, Background = Brush(isActive ? "#19172f" : "#0e1530"), BorderBrush = Brush(isActive ? "#65448c" : "#253056"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(4, 0, 4, 8), Height = 66 });
        }
        if (cards.Children.Count == 0) cards.Children.Add(Card(Text(l["empty"])));
        var env = Environment.GetEnvironmentVariable("XR_RUNTIME_JSON");
        if (!string.IsNullOrEmpty(env)) cards.Children.Add(Card(Text(l["override"], 13, "#ffd08a")));
        if (!string.IsNullOrEmpty(active) && !prefs.Runtimes.Any(r => RuntimeRegistry.Same(r.ResolvedPath, active)))
            cards.Children.Add(Card(Text(l["unlistedActive"], 12, "#8e98b8")));
        // Restore automatic height after a manual resize when the visible list changes.
        if (WindowState == WindowState.Normal) SizeToContent = SizeToContent.Height;
    }
    async Task Activate(RuntimeEntry runtime)
    {
        try
        {
            if (!RuntimeRegistry.Validate(runtime.ResolvedPath, out var error)) throw new InvalidDataException(error);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--set-runtime"); start.ArgumentList.Add(runtime.ResolvedPath);
            using var process = Process.Start(start) ?? throw new IOException(l["switchFailed"]); await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !RuntimeRegistry.Same(RuntimeRegistry.Active, runtime.ResolvedPath)) throw new IOException(l["switchFailed"]);
            Refresh(); status.Text = l["switched"] + " " + runtime.Name;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { status.Text = l["cancelled"]; }
        catch (Exception ex) { Error(ex); }
    }
    Window Dialog(string title, double width = 620, double height = 520) => new() { Title = title, Owner = this, Width = width, Height = height, MinWidth = width, MinHeight = height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
    void Settings()
    {
        var window = new Window { Title = l["languageSettings"], Owner = this, Width = 330, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        var label = Text(l["language"], 13); panel.Children.Add(label);
        var combo = new ComboBox { Width = 260, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 13, SelectedValuePath = "Id" }; panel.Children.Add(combo);
        var hint = Text(l["languageImmediate"], 12, "#8e98b8"); hint.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(hint);
        var languages = Directory.GetFiles(System.IO.Path.Combine(Store.Root, "lang"), "*.json").Select(System.IO.Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(id => id).ToList();
        bool updating = false;
        void Populate()
        {
            updating = true;
            combo.ItemsSource = languages.Select(id => new LanguageChoice(id, id.ToUpperInvariant() + ": " + (id is "de" or "en" or "fr" ? l["language_" + id] : LanguageName(id)))).ToList();
            combo.SelectedValue = prefs.Language;
            window.Title = l["languageSettings"]; label.Text = l["language"]; hint.Text = l["languageImmediate"];
            updating = false;
        }
        Populate();
        combo.SelectionChanged += (_, _) =>
        {
            if (updating || combo.SelectedValue is not string id || id == prefs.Language) return;
            var previous = prefs.Language;
            try { new Language().Load(id); prefs.Language = id; Store.Save(prefs); Build(); Populate(); }
            catch (Exception ex) { prefs.Language = previous; l.Load(previous); Populate(); MessageBox.Show(window, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        window.ShowDialog();
    }
    sealed record LanguageChoice(string Id, string Label) { public override string ToString() => Label; }
    static string LanguageName(string id)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(id).NativeName; }
        catch (System.Globalization.CultureNotFoundException) { return id.ToUpperInvariant(); }
    }
    void Manage()
    {
        var window = new Window { Title = l["manage"], Owner = this, Width = 570, MinWidth = 460, MinHeight = 150, MaxHeight = Math.Max(150, SystemParameters.WorkArea.Height - 24), SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var dock = new DockPanel { Margin = new Thickness(14) }; window.Content = dock;
        var hint = Text(l["manageHintCompact"], 12, "#8e98b8"); hint.Margin = new Thickness(0, 0, 0, 10); hint.ToolTip = l["manageHint"]; DockPanel.SetDock(hint, Dock.Top); dock.Children.Add(hint);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bar, Dock.Bottom); dock.Children.Add(bar);
        var list = new StackPanel(); dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Populate()
        {
            list.Children.Clear();
            foreach (var r in prefs.Runtimes)
            {
                var row = new DockPanel();
                var edit = Button(l["edit"], () => { Edit(r, window); Populate(); Refresh(); }); edit.FontSize = 12; edit.Margin = new Thickness(0); DockPanel.SetDock(edit, Dock.Right); row.Children.Add(edit);
                var check = new CheckBox { Content = r.Name + (r.Installed ? "" : "  ·  " + l["missing"]), IsChecked = r.Visible, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(0, 0, 8, 0) };
                check.Click += (_, _) => Safe(() => { r.Visible = check.IsChecked == true; Store.Save(prefs); Refresh(); }); row.Children.Add(check);
                list.Children.Add(new Border { Child = row, Background = Brush("#0e1530"), CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 5, 6, 5), Margin = new Thickness(0, 0, 0, 4) });
            }
            if (window.WindowState == WindowState.Normal) window.SizeToContent = SizeToContent.Height;
        }
        bar.Children.Add(Button(l["add"], () => { Edit(null, window); Populate(); Refresh(); }));
        bar.Children.Add(Button(l["refresh"], () => Safe(() => { RuntimeRegistry.Discover(prefs); Store.Save(prefs); Populate(); Refresh(); })));
        bar.Children.Add(Button(l["close"], () => window.Close())); Populate(); window.ShowDialog();
    }
    void Edit(RuntimeEntry? original, Window owner)
    {
        var window = Dialog(l["edit"], 680, 610); window.Owner = owner;
        var panel = new StackPanel { Margin = new Thickness(26) }; window.Content = panel;
        panel.Children.Add(Text(original == null ? l["add"] : l["edit"], 24)); panel.Children.Add(Text(l["name"]));
        var name = new TextBox { Text = original?.Name ?? "" }; panel.Children.Add(name);
        panel.Children.Add(Text(l["manifest"])); var path = new TextBox { Text = original?.ResolvedPath ?? "" }; panel.Children.Add(path);
        var logo = new TextBox { Text = original == null ? "" : Store.Resolve(original.Logo) };
        var browse = Button(l["browseManifest"], () => { var picker = new OpenFileDialog { Filter = "OpenXR manifest (*.json)|*.json" }; if (picker.ShowDialog(window) == true) path.Text = picker.FileName; }); panel.Children.Add(browse);
        panel.Children.Add(Text(l["logo"], 14)); panel.Children.Add(logo);
        var browseLogo = Button(l["browseLogo"], () => { var picker = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp" }; if (picker.ShowDialog(window) == true) logo.Text = picker.FileName; }); panel.Children.Add(browseLogo);
        var visible = new CheckBox { Content = l["visible"], IsChecked = original?.Visible ?? true }; panel.Children.Add(visible);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(buttons);
        buttons.Children.Add(Button(l["save"], () => Safe(() =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) throw new InvalidDataException(l["nameRequired"]);
            var full = string.IsNullOrWhiteSpace(path.Text) ? "" : System.IO.Path.GetFullPath(path.Text.Trim());
            if ((original == null || full != original.ResolvedPath) && !RuntimeRegistry.Validate(full, out _)) throw new InvalidDataException(l["invalid"]);
            if (prefs.Runtimes.Any(r => r != original && RuntimeRegistry.Same(r.ResolvedPath, full))) throw new InvalidDataException(l["duplicate"]);
            var entry = original ?? new RuntimeEntry { Custom = true }; var logoValue = "";
            if (!string.IsNullOrWhiteSpace(logo.Text))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.UriSource = new Uri(System.IO.Path.GetFullPath(logo.Text)); bitmap.EndInit();
                var dest = System.IO.Path.Combine(Store.Data, "logos", entry.Id + ".png"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var stream = File.Create(dest)) encoder.Save(stream); logoValue = Store.PortablePath(dest);
            }
            entry.Name = name.Text.Trim(); entry.Path = Store.PortablePath(full); entry.Logo = logoValue; entry.Visible = visible.IsChecked == true;
            if (original == null) prefs.Runtimes.Add(entry); Store.Save(prefs); window.Close();
        })));
        buttons.Children.Add(Button(l["cancel"], () => window.Close()));
        if (original?.Custom == true) buttons.Children.Add(Button(l["delete"], () => Safe(() => { if (MessageBox.Show(window, l["deleteConfirm"], Title, MessageBoxButton.YesNo) == MessageBoxResult.Yes) { prefs.Runtimes.Remove(original); Store.Save(prefs); window.Close(); } })));
        window.ShowDialog();
    }
    async Task CheckUpdates(bool manual)
    {
        if (checking) return; checking = true; status.Text = l["checking"];
        try
        {
            var release = await Updater.Check(); status.Text = l["upToDate"];
            if (release == null) { if (manual) MessageBox.Show(this, l["upToDate"], Title); return; }
            status.Text = l["updateAvailable"] + " " + release.Version;
            if (MessageBox.Show(this, l["updatePrompt"] + "\n\nv" + release.Version, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            status.Text = l["downloading"]; await Updater.Install(release); Application.Current.Shutdown();
        }
        catch (Exception ex) { status.Text = l["updateFailed"]; if (manual) Error(ex); }
        finally { checking = false; }
    }
    void About()
    {
        var window = new Window { Title = l["about"], Owner = this, Width = 490, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        panel.Children.Add(Text("OpenXR Changer · v" + Updater.Version, 19, "#ffffff"));
        panel.Children.Add(Text("Urs Caviezel", 13, "#8e98b8"));
        panel.Children.Add(Text(l["aboutText"], 13));
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("GitHub · urscaviezel/OpenXR-Changer")) { NavigateUri = new Uri(Updater.Repository), Foreground = Brush("#22d3ff") };
        link.RequestNavigate += (_, e) => { Safe(() => Open(e.Uri.AbsoluteUri)); e.Handled = true; };
        var linkText = new TextBlock { Margin = new Thickness(0, 6, 0, 16) }; linkText.Inlines.Add(link); panel.Children.Add(linkText);
        panel.Children.Add(Text("Based on OpenXR Runtime Switcher by WaGi-Coding (MIT).\nDetection credits: KOGA Mitsuhiro.", 12, "#8e98b8"));
        var close = Button(l["close"], () => window.Close()); close.HorizontalAlignment = HorizontalAlignment.Right; close.Margin = new Thickness(0, 10, 0, 0); panel.Children.Add(close);
        window.ShowDialog();
    }
    static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    void Error(Exception ex) => MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
    void Safe(Action action) { try { action(); } catch (Exception ex) { Error(ex); } }
}
