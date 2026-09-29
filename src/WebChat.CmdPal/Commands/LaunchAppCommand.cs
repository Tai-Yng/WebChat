// Copyright (c) Tai-Yng. MIT license.

using System;
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace WebChat.CmdPal.Commands;

/// <summary>Launches the WebChat App window (needed once to log in; tray-resident afterwards).</summary>
public sealed partial class LaunchAppCommand : InvokableCommand
{
    /// <summary>Package family suffix is deterministic for publisher CN=Tai-Yng.</summary>
    public const string AppAumid = "TaiYng.WebChat.App_511vm1e3pntc2!App";

    private readonly Action<string> _launch;

    public LaunchAppCommand() : this(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
    {
    }

    internal LaunchAppCommand(Action<string> launch)
    {
        _launch = launch;
        Name = "启动 WebChat 窗口";
        Icon = new IconInfo("\uE8F2");
    }

    public override CommandResult Invoke()
    {
        try
        {
            _launch("shell:AppsFolder\\" + AppAumid);
        }
        catch (Exception ex)
        {
            return CommandResult.ShowToast(new ToastArgs
            {
                Message = $"启动失败: {ex.Message}",
                Result = CommandResult.Dismiss(),
            });
        }
        return CommandResult.Dismiss();
    }
}
