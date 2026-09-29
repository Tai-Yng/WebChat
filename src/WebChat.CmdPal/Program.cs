// Based on the official CmdPal ExtensionTemplate (microsoft/PowerToys, MIT).
// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebChat.CmdPal;

public class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // Single instance of the extension object, returned on every host request.
            WebChatExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<WebChatExtension, IExtension>(() => extensionInstance);
            server.Start();

            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Extension... exiting.");
        }
    }
}
