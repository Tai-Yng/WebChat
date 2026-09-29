// Copyright (c) Tai-Yng. MIT license.

using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace WebChat.App;

public partial class App : Application
{
    public static bool IsExiting { get; internal set; }
    private const string DeepSeekUrl = "https://chat.deepseek.com/";

    private static Mutex? _singleInstanceMutex;
    private static EventWaitHandle? _activateSignal;
    private static MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(true, @"Local\WebChat.SingleInstance", out var createdNew);
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WebChat.Activate");

        if (!createdNew)
        {
            // A second launch (e.g. from the CmdPal command) just wakes the first window.
            _activateSignal.Set();
            Shutdown();
            return;
        }

        var iconStream = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        var trayIcon = new System.Drawing.Icon(iconStream);

        _mainWindow = new MainWindow(DeepSeekUrl, trayIcon);
        _mainWindow.HideToTrayRequested += () => ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _mainWindow.Show();

        _ = new Controller(trayIcon, _mainWindow);

        // Wake the window whenever another process asks for it.
        new Thread(() =>
        {
            while (_activateSignal.WaitOne())
            {
                Dispatcher.Invoke(() => _mainWindow?.Wake());
            }
        })
        { IsBackground = true }.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}

/// <summary>Owns the tray icon; keeps the process alive when the window is hidden to tray.</summary>
internal sealed class Controller : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _tray;
    private readonly MainWindow _window;

    public Controller(System.Drawing.Icon icon, MainWindow window)
    {
        _window = window;
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "WebChat — DeepSeek",
            Visible = true,
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 WebChat", null, (_, _) => _window.Wake());
        var pinItem = new System.Windows.Forms.ToolStripMenuItem("置顶");
        pinItem.Click += (_, _) =>
        {
            _window.TogglePin();
            pinItem.Checked = _window.IsPinned;
        };
        menu.Items.Add(pinItem);
        menu.Items.Add("退出", null, (_, _) =>
        {
            _tray.Visible = false;
            App.IsExiting = true;
            Application.Current.Shutdown();
        });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => _window.Wake();
    }

    public void Dispose() => _tray.Dispose();
}
