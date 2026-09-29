// Copyright (c) Tai-Yng. MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebChat.Protocol;

/// <summary>Client for the WebChat.App local pipe. One connection per operation.</summary>
public static class ChatPipeClient
{
    public static async Task<List<ChatMessage>> HistoryAsync(CancellationToken ct)
    {
        var env = await RequestAsync("{\"op\":\"history\"}", readLines: false, onLine: null, ct);
        return env.Messages;
    }

    public static async Task ClearAsync(CancellationToken ct) =>
        await RequestAsync("{\"op\":\"clear\"}", readLines: false, onLine: null, ct);

    /// <summary>
    /// Sends a message; consumes delta lines, invoking (fullTextSoFar, done) per delta;
    /// returns the final text. Throws IOException on error envelopes.
    /// </summary>
    public static async Task<string> SendAsync(string text, Action<string, bool>? delta, CancellationToken ct)
    {
        var request = System.Text.Json.JsonSerializer.Serialize(new { op = "send", text }, ChatProtocol.Options);
        var final = string.Empty;
        await RequestAsync(request, readLines: true, line =>
        {
            var env = ChatProtocol.Parse(line);
            if (env.Op == "error")
            {
                throw new IOException(env.Text);
            }
            if (env.Op == "delta")
            {
                final = env.Text;
                delta?.Invoke(env.Text, env.Done);
                return env.Done; // stop reading on the final delta
            }
            return false;
        }, ct);
        return final;
    }

    private static async Task<ChatProtocol.Envelope> RequestAsync(
        string requestLine, bool readLines, Func<string, bool>? onLine, CancellationToken ct)
    {
        await using var pipe = new NamedPipeClientStream(".", ChatProtocol.PipeName, PipeDirection.InOut);
        await pipe.ConnectAsync(3000, ct);

        await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync(requestLine);

        var first = await reader.ReadLineAsync(ct) ?? throw new IOException("pipe closed");
        var env = ChatProtocol.Parse(first);
        if (env.Op == "error")
        {
            throw new IOException(env.Text);
        }

        if (readLines)
        {
            while (!env.Done)
            {
                var line = await reader.ReadLineAsync(ct) ?? throw new IOException("pipe closed");
                if (onLine?.Invoke(line) == true)
                {
                    env = ChatProtocol.Parse(line);
                    continue;
                }
                env = ChatProtocol.Parse(line);
                if (env.Op == "error")
                {
                    throw new IOException(env.Text);
                }
            }
        }

        return env;
    }
}
