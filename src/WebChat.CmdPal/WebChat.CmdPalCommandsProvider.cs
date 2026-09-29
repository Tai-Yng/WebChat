// Based on the official CmdPal ExtensionTemplate (microsoft/PowerToys, MIT).
// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WebChat.CmdPal.Commands;

namespace WebChat.CmdPal;

public partial class WebChatCommandsProvider : CommandProvider
{
    private readonly ICommandItem[] _commands;

    public WebChatCommandsProvider()
    {
        DisplayName = "WebChat";
        Icon = IconHelpers.FromRelativePath("Assets\\icon.png");
        _commands =
        [
            new CommandItem(new ChatPage())
            {
                Title = DisplayName,
                Subtitle = "在调色板里直接对话 DeepSeek（网页会话，免 API token）",
                MoreCommands = [new CommandContextItem(new LaunchAppCommand())],
            },
        ];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }
}
