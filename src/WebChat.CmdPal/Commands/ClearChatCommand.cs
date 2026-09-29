// Copyright (c) Tai-Yng. MIT license.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WebChat.Protocol;

namespace WebChat.CmdPal.Commands;

/// <summary>Clears the conversation (both in the App and the persisted history).</summary>
public sealed partial class ClearChatCommand : InvokableCommand
{
    private readonly ChatPage _page;

    public ClearChatCommand(ChatPage page)
    {
        _page = page;
        Name = "清空对话";
        Icon = new IconInfo("\uE74D"); // Delete glyph
    }

    public override CommandResult Invoke()
    {
        var ok = _page.BeginClear();
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = ok ? "对话已清空" : "WebChat 未运行，无法清空",
            Result = CommandResult.Dismiss(),
        });
    }
}
