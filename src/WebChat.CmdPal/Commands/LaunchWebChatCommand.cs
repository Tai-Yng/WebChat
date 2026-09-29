// Copyright (c) Tai-Yng. MIT license.

using System;
using System.Diagnostics;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace WebChat.CmdPal.Commands;

/// <summary>Launches (or, via the app's single-instance logic, activates) the WebChat window.</summary>
public sealed partial class LaunchWebChatCommand : InvokableCommand
{
    /// <summary>Package family name suffix is deterministic for publisher CN=Tai-Yng.</summary>
    public const string AppAumid = "TaiYng.WebChat.App_511vm1e3pntc2!App";

    private readonly Action<string> _launch;

    public LaunchWebChatCommand() : this(url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }))
    {
    }

    internal LaunchWebChatCommand(Action<string> launch)
    {
        _launch = launch;
        Name = "Open WebChat";
        Icon = new IconInfo("\uE8F2"); // Chat bubble glyph
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
                Message = $"Failed to launch WebChat: {ex.Message}",
                Result = CommandResult.Dismiss(),
            });
        }

        return CommandResult.Dismiss();
    }
}
