// Copyright (c) Tai-Yng. MIT license.
//
// Loopback TCP transport for the chat protocol. Named pipes are unusable here because
// MSIX packages get per-package named-object namespaces (an extension in one package
// cannot open a pipe created by an app in another package); loopback TCP has no such
// isolation. Server binds 127.0.0.1 only; a shared token file gates access.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebChat.Protocol;

public static class ChatWire
{
    public const int DefaultPort = 47915;
    public static readonly int[] PortCandidates = [DefaultPort, 47916, 47917, 47918];

    /// <summary>Shared handshake token; the App writes it, the extension reads it.</summary>
    public static string TokenPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WebChat", "session-token.txt");

    public static string ReadToken()
    {
        try
        {
            return File.ReadAllText(TokenPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    public static string WriteToken()
    {
        var path = TokenPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length > 0)
            {
                return existing;
            }
        }
        var token = Guid.NewGuid().ToString("N");
        File.WriteAllText(path, token);
        return token;
    }
}

/// <summary>Client for the WebChat.App loopback chat server. One connection per operation.</summary>
public static class ChatTcpClient
{
    public static async Task<List<ChatMessage>> HistoryAsync(CancellationToken ct)
    {
        var env = await RequestAsync("""{"op":"history"}""", onDelta: null, ct);
        return env.Messages;
    }

    public static async Task ClearAsync(CancellationToken ct) =>
        await RequestAsync("""{"op":"clear"}""", onDelta: null, ct);

    /// <summary>
    /// Sends a message; consumes delta lines, invoking (fullTextSoFar, done) per delta;
    /// returns the final text. Throws IOException on transport or error envelopes.
    /// </summary>
    public static async Task<string> SendAsync(string text, Action<string, bool>? delta, CancellationToken ct)
    {
        var request = System.Text.Json.JsonSerializer.Serialize(new { op = "send", text }, ChatProtocol.Options);
        var env = await RequestAsync(request, delta, ct);
        return env.Text;
    }

    private static async Task<ChatProtocol.Envelope> RequestAsync(
        string requestLine, Action<string, bool>? onDelta, CancellationToken ct)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, ChatWire.DefaultPort, ct);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var token = ChatWire.ReadToken();
        if (token.Length == 0)
        {
            throw new IOException("WebChat handshake token not found — is the app installed and running?");
        }

        // Line 1: handshake. Line 2: the request.
        await writer.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(
            new { op = "hello", text = token }, ChatProtocol.Options));
        await writer.WriteLineAsync(requestLine);

        var env = await ReadEnvelopeAsync(reader, ct);
        if (env.Op == "error")
        {
            throw new IOException(env.Text);
        }

        var final = string.Empty;
        while (!env.Done)
        {
            env = await ReadEnvelopeAsync(reader, ct);
            if (env.Op == "error")
            {
                throw new IOException(env.Text);
            }
            if (env.Op == "delta")
            {
                final = env.Text;
                onDelta?.Invoke(env.Text, env.Done);
            }
        }

        return env with { Text = final };
    }

    private static async Task<ChatProtocol.Envelope> ReadEnvelopeAsync(StreamReader reader, CancellationToken ct)
    {
        var line = await reader.ReadLineAsync(ct) ?? throw new IOException("connection closed");
        return ChatProtocol.Parse(line);
    }
}
