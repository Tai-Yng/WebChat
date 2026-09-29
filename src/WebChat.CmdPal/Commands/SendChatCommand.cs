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
        var result = _page.BeginSend(_text);
        var message = result switch
        {
            ChatPage.SendResult.Started => "已发送，回复生成中…",
            ChatPage.SendResult.Connecting => "正在连接 WebChat…连上后这条消息会自动发送",
            ChatPage.SendResult.Busy => "上一条回复还在生成中，稍候再发",
            ChatPage.SendResult.Offline => "WebChat 未运行，请先点列表里的启动项",
            _ => "未知状态",
        };
        return CommandResult.ShowToast(new ToastArgs
        {
            Message = message,
            Result = CommandResult.Dismiss(),
        });
    }

    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
