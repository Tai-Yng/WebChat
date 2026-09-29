// Copyright (c) Tai-Yng. MIT license.

using System;
using System.Threading.Tasks;
using System.Windows;
using WebChat.Protocol;
using Microsoft.Web.WebView2.Core;

namespace WebChat.App;

public partial class MainWindow : Window
{
    private readonly string _startUrl;
    private readonly System.Drawing.Icon _trayIcon;

    public event Action? HideToTrayRequested;

    public bool IsPinned => Topmost;

    public MainWindow(string startUrl, System.Drawing.Icon trayIcon)
    {
        InitializeComponent();
        _startUrl = startUrl;
        _trayIcon = trayIcon;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            Application.GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream);
        Loaded += async (_, _) => await InitializeWebAsync();
    }

    private async Task InitializeWebAsync()
    {
        var boot = BootLog.Instance;

        // Dedicated user-data folder: logging in once here persists independently
        // of any browser profile cleanup.
        var userDataFolder = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WebChat", "WebView2");
        boot.Step("userdata: " + userDataFolder);

        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: userDataFolder, options: new CoreWebView2EnvironmentOptions());
            boot.Step("environment created");
            await Web.EnsureCoreWebView2Async(environment);
            boot.Step("webview ready");
        }
        catch (Exception ex)
        {
            boot.Step("webview FAILED: " + ex.Message);
            throw;
        }

        Web.CoreWebView2.DocumentTitleChanged += (_, _) =>
        {
            Title = Web.CoreWebView2.DocumentTitle;
            TitleText.Text = Title.Length > 0 ? $"WebChat · {Title}" : "WebChat";
        };
        Web.Source = new Uri(_startUrl);
        boot.Step("navigating: " + _startUrl);

        // Chat stack: DOM bridge + conversation log + local pipe server for the CmdPal UI.
        try
        {
            var localState = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            boot.Step("localstate: " + localState);
            var chat = new ChatService(
                new DeepSeekBridge(Web),
                new ChatLog(System.IO.Path.Combine(localState, "chat-history.json")));
            boot.Step("chat service created");
            new ChatTcpServer(chat, ChatWire.WriteToken()).Start();
            boot.Step("chat tcp server started");
        }
        catch (Exception ex)
        {
            boot.Step("chat stack FAILED: " + ex.Message);
        }
    }

    public void TogglePin() => Topmost = !Topmost;

    public void Wake()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
        Topmost = Topmost; // re-assert z-order
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => TogglePin();

    private void Tray_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        HideToTrayRequested?.Invoke();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The close button hides to tray; real exit lives in the tray menu.
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            HideToTrayRequested?.Invoke();
        }
        base.OnClosing(e);
    }
}
