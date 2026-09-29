// Copyright (c) Tai-Yng. MIT license.

using System;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WebChat.Protocol;

namespace WebChat.CmdPal.Commands;

/// <summary>
/// Sends a chat message through the pipe and refreshes the page while the answer streams.
/// The send itself runs in the background; Invoke returns immediately.
/// </summary>
public sealed partial class SendChatCommand : InvokableCommand
{
    private readonly ChatPage _page;
    private readonly string _text;

    public SendChatCommand(ChatPage page, string text)
    {
        _page = page;
        _text = text;
        Name = $"发送: {Truncate(text, 40)}";
        Icon = new IconInfo("\uE724"); // Send glyph
    }

    public override CommandResult Invoke()
    {
        var accepted = _page.BeginSend(_text);
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = accepted ? "已发送，回复生成中…" : "WebChat 正忙或未运行",
            Result = CommandResult.Dismiss(),
        });
    }

    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
