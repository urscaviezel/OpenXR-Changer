using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace OpenXRChanger;

public sealed partial class MainWindow
{
    Forms.NotifyIcon? tray;
    Forms.ContextMenuStrip? trayMenu;
    System.Drawing.Icon? trayIcon;
    bool exiting, switching;
    public static bool IsAdministrator => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    void InitializeTray()
    {
        trayIcon = new System.Drawing.Icon(Path.Combine(Store.Root, "assets", "OpenXR-Changer.ico"));
        trayMenu = new Forms.ContextMenuStrip { ShowImageMargin = true, ShowCheckMargin = true, ImageScalingSize = new System.Drawing.Size(16, 16) };
        trayMenu.Renderer = new TrayRenderer();
        trayMenu.ForeColor = System.Drawing.Color.FromArgb(220, 225, 240);
        trayMenu.Opening += (_, _) => RebuildTrayMenu();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "OpenXR Changer", ContextMenuStrip = trayMenu, Visible = true };
        tray.DoubleClick += (_, _) => ShowMainWindow();
        Closing += (_, e) => { if (!exiting && prefs.CloseToTray) { e.Cancel = true; Hide(); } };
        Closed += (_, _) => DisposeTray();
        Application.Current.Exit += (_, _) => DisposeTray();
        Application.Current.SessionEnding += (_, _) => exiting = true;
    }

    public void Start(bool trayOnly)
    {
        // Create the handle even when hidden so second-instance activation works.
        _ = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        if (!trayOnly && !prefs.StartInTray) Show();
        Dispatcher.BeginInvoke(new Action(async () => { if (prefs.CheckUpdates) await CheckUpdates(false); }));
    }

    void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    void DisposeTray()
    {
        if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
        ClearTrayItems();
        trayMenu?.Dispose(); trayMenu = null;
        trayIcon?.Dispose(); trayIcon = null;
    }

    void ExitApp() { exiting = true; Application.Current.Shutdown(); }

    void ClearTrayItems()
    {
        if (trayMenu == null) return;
        foreach (var item in trayMenu.Items.Cast<Forms.ToolStripItem>().ToArray())
        {
            var image = item.Image;
            item.Image = null;
            image?.Dispose();
            item.Dispose();
        }
        trayMenu.Items.Clear();
    }

    void RebuildTrayMenu()
    {
        if (trayMenu == null) return;
        ClearTrayItems();
        var current = RuntimeRegistry.Active;
        foreach (var runtime in prefs.Runtimes.Where(r => r.Visible).OrderBy(r => r.Name))
        {
            bool selected = RuntimeRegistry.Same(runtime.ResolvedPath, current);
            bool installed = runtime.Installed;
            var item = new Forms.ToolStripMenuItem(runtime.Name.Replace("&", "&&") + (installed ? "" : " · " + l["missing"]))
            { Checked = selected, Enabled = installed && !switching, Image = RuntimeLogo.CreateTrayBitmap(runtime) };
            item.Click += async (_, _) => { if (!RuntimeRegistry.Same(runtime.ResolvedPath, RuntimeRegistry.Active)) await Activate(runtime); };
            trayMenu.Items.Add(item);
        }
        if (trayMenu.Items.Count == 0) trayMenu.Items.Add(new Forms.ToolStripMenuItem(l["trayEmpty"]) { Enabled = false });
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add(l["trayOpen"], null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add(l["exit"], null, (_, _) => ExitApp());
    }

    void AddTraySettings(MenuItem settings)
    {
        var parent = new MenuItem { Header = l["traySettings"] };
        void Toggle(string key, Func<bool> get, Action<bool> set)
        {
            var item = new MenuItem { Header = l[key], IsCheckable = true, IsChecked = get(), StaysOpenOnClick = true };
            item.Click += (_, _) => Safe(() => { var old = get(); set(item.IsChecked); try { Store.Save(prefs); } catch { set(old); item.IsChecked = old; throw; } });
            parent.Items.Add(item);
        }
        Toggle("startInTray", () => prefs.StartInTray, value => prefs.StartInTray = value);
        Toggle("closeToTray", () => prefs.CloseToTray, value => prefs.CloseToTray = value);
        settings.Items.Add(parent);
        var helper = new MenuItem { Header = l["helperSettings"] };
        helper.Click += (_, _) => HelperSettings();
        settings.Items.Add(helper);
        var admin = new MenuItem { Header = l[IsAdministrator ? "adminEnabled" : "adminSession"], IsEnabled = !IsAdministrator };
        admin.ToolTip = l["adminHint"];
        admin.Click += (_, _) => RestartAsAdministrator();
        settings.Items.Add(admin);
    }

    void RestartAsAdministrator()
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Store.Root };
            start.ArgumentList.Add("--restart-admin"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var process = Process.Start(start) ?? throw new IOException(l["switchFailed"]);
            ExitApp();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { status.Text = l["cancelled"]; }
        catch (Exception ex) { Error(ex); }
    }

    void HelperSettings()
    {
        var window = new Window { Title = l["helperSettings"], Owner = this, Width = 470, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; window.Content = panel;
        panel.Children.Add(Text(l["helperExplanation"], 13));
        var state = Text(l[HelperClient.IsConfigured ? "helperConfigured" : "helperNotConfigured"], 12, "#8e98b8"); panel.Children.Add(state);
        var setup = Button(l["helperSetup"], () => { });
        var check = Button(l["helperCheck"], () => { });
        var remove = Button(l["helperUninstall"], () => { });
        void Busy(bool busy)
        {
            setup.IsEnabled = check.IsEnabled = !busy;
            remove.IsEnabled = !busy && HelperClient.CanUninstall;
        }
        setup.Click += async (_, _) =>
        {
            Busy(true);
            try { await HelperClient.Setup(); state.Text = l["helperReady"]; }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { state.Text = l["cancelled"]; }
            catch (Exception ex) { state.Text = l["helperFailed"] + "\n\n" + ex.Message; }
            finally { Busy(false); }
        };
        check.Click += async (_, _) =>
        {
            Busy(true);
            try { await HelperClient.Send("ping"); state.Text = l["helperReady"]; }
            catch (Exception ex) { state.Text = l["helperFailed"] + "\n\n" + ex.Message; }
            finally { Busy(false); }
        };
        remove.Click += async (_, _) =>
        {
            Busy(true);
            try { await HelperClient.Uninstall(); state.Text = l["helperRemoved"]; }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { state.Text = l["cancelled"]; }
            catch (Exception ex) { state.Text = l["helperRemoveFailed"] + "\n\n" + ex.Message; }
            finally { Busy(false); }
        };
        panel.Children.Add(setup); panel.Children.Add(check); panel.Children.Add(remove);
        Busy(false);
        var close = Button(l["close"], () => window.Close()); close.HorizontalAlignment = HorizontalAlignment.Right; panel.Children.Add(close);
        window.ShowDialog();
    }

    sealed class TrayRenderer : Forms.ToolStripProfessionalRenderer
    {
        static readonly System.Drawing.Color ActiveGreen = System.Drawing.Color.FromArgb(92, 220, 139);
        public TrayRenderer() : base(new TrayColors()) { }

        protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
        {
            // Replace the standard check and its square background with a small dot.
            float diameter = 6f * e.Graphics.DpiX / 96f;
            var bounds = e.ImageRectangle;
            var previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new System.Drawing.SolidBrush(ActiveGreen);
            e.Graphics.FillEllipse(brush, bounds.X + (bounds.Width - diameter) / 2f,
                bounds.Y + (bounds.Height - diameter) / 2f, diameter, diameter);
            e.Graphics.SmoothingMode = previous;
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is Forms.ToolStripMenuItem { Checked: true })
                Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, ActiveGreen, e.TextFormat);
            else Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle,
                e.Item.Enabled ? System.Drawing.Color.FromArgb(238, 242, 255) : System.Drawing.Color.FromArgb(142, 152, 184), e.TextFormat);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (e.Item is Forms.ToolStripMenuItem { Checked: true })
            {
                using var brush = new System.Drawing.SolidBrush(e.Item.Selected
                    ? System.Drawing.Color.FromArgb(28, 65, 48)
                    : System.Drawing.Color.FromArgb(19, 42, 35));
                e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(1, 0, e.Item.Width - 2, e.Item.Height));
            }
            else
            {
                using var brush = new System.Drawing.SolidBrush(e.Item.Selected
                    ? System.Drawing.Color.FromArgb(40, 51, 78)
                    : System.Drawing.Color.FromArgb(14, 21, 48));
                e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(0, 0, e.Item.Width, e.Item.Height));
            }
        }
    }

    sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public override System.Drawing.Color ToolStripDropDownBackground => System.Drawing.Color.FromArgb(14, 21, 48);
        public override System.Drawing.Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override System.Drawing.Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override System.Drawing.Color MenuItemSelected => System.Drawing.Color.FromArgb(30, 38, 58);
        public override System.Drawing.Color MenuItemBorder => System.Drawing.Color.FromArgb(63, 77, 101);
        public override System.Drawing.Color CheckBackground => MenuItemSelected;
        public override System.Drawing.Color CheckSelectedBackground => MenuItemSelected;
    }
}
