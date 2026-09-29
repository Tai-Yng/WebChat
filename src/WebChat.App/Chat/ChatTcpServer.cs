// Copyright (c) Tai-Yng. MIT license.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebChat.Protocol;

namespace WebChat.App;

/// <summary>
/// Loopback TCP server for the CmdPal chat UI (named pipes are namespaced per MSIX
/// package, so cross-package clients could never reach them). Binds 127.0.0.1 only.
/// Protocol: line 1 = {"op":"hello","text":"<token>"}, line 2 = the actual request;
/// responses are one or more JSON lines.
/// </summary>
public sealed class ChatTcpServer
{
    private readonly ChatService _chat;
    private readonly DeepSeekBridge _bridge;
    private readonly string _token;

    public ChatTcpServer(ChatService chat, DeepSeekBridge bridge, string token)
    {
        _chat = chat;
        _bridge = bridge;
        _token = token;
    }

    public void Start()
    {
        foreach (var port in ChatWire.PortCandidates)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                _ = AcceptLoopAsync(listener);
                BootLog.Instance.Step($"chat tcp server listening on 127.0.0.1:{port}");
                return;
            }
            catch (Exception ex)
            {
                BootLog.Instance.Step($"port {port} unavailable: {ex.Message}");
            }
        }
        BootLog.Instance.Step("chat tcp server FAILED: no port available");
    }

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch
            {
                return; // listener disposed on shutdown
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            await using var stream = client.GetStream();
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

            // Line 1: handshake.
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var hello = ChatProtocol.Parse(line);
            if (hello.Op != "hello" || hello.Text != _token)
            {
                await writer.WriteLineAsync(ChatProtocol.Error("unauthorized"));
                return;
            }

            // Line 2: the actual request.
            line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            var env = ChatProtocol.Parse(line);
            switch (env.Op)
            {
                case "history":
                    await writer.WriteLineAsync(ChatProtocol.History(_chat.Messages));
                    break;

                case "inspect":
                    await writer.WriteLineAsync(ChatProtocol.Delta(await _bridge.InspectAsync(), true));
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
            BootLog.Instance.Step("tcp client disconnected mid-stream");
        }
        catch (Exception ex)
        {
            BootLog.Instance.Step("tcp handler FAILED: " + ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }
}
