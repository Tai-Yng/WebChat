// Copyright (c) Tai-Yng. MIT license.

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebChat.Protocol;

namespace WebChat.App;

/// <summary>
/// Local named-pipe server: one operation per connection, one or more JSON lines back.
/// Only local processes can connect (default pipe security).
/// </summary>
public sealed class ChatPipeServer
{
    private readonly ChatService _chat;

    public ChatPipeServer(ChatService chat)
    {
        _chat = chat;
    }

    public void Start()
    {
        _ = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    ChatProtocol.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var env = ChatProtocol.Parse(line);
                switch (env.Op)
                {
                    case "history":
                        await writer.WriteLineAsync(ChatProtocol.History(_chat.Messages));
                        break;

                    case "clear":
                        _chat.Clear();
                        await writer.WriteLineAsync(ChatProtocol.Ok());
                        break;

                    case "send":
                        if (_chat.IsBusy)
                        {
                            await writer.WriteLineAsync(ChatProtocol.Error("busy"));
                            break;
                        }

                        await writer.WriteLineAsync(ChatProtocol.Accepted());
                        await _chat.SendAsync(
                            env.Text,
                            (full, done) => writer.WriteLineAsync(ChatProtocol.Delta(full, done)).GetAwaiter().GetResult(),
                            CancellationToken.None);
                        break;

                    default:
                        await writer.WriteLineAsync(ChatProtocol.Error($"unknown op: {env.Op}"));
                        break;
                }
            }
            catch (IOException)
            {
                // client disconnected mid-stream — accept the next one
            }
            catch (Exception)
            {
                // keep the server alive no matter what
            }
        }
    }
}
