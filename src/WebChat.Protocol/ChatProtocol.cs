// Copyright (c) Tai-Yng. MIT license.
//
// Shared chat protocol between WebChat.App (hosts the DeepSeek web session) and
// WebChat.CmdPal (chat UI). One named-pipe connection per operation; the server
// answers with one or more JSON lines.

using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WebChat.Protocol;

public sealed record ChatMessage(string Role, string Text, long AtUnixMs)
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

/// <summary>JSON-lines envelopes. Line format: {"op": "...", ...}.</summary>
public static class ChatProtocol
{
    public const string PipeName = "webchat-chat";

    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string History(IEnumerable<ChatMessage> messages)
    {
        // Explicit lowercase projection — never depend on naming-policy/casing behavior.
        var items = messages.Select(m => new { role = m.Role, text = m.Text, at = m.AtUnixMs });
        return JsonSerializer.Serialize(new { op = "history", messages = items }, Options);
    }

    public static string Accepted() =>
        JsonSerializer.Serialize(new { op = "accepted" }, Options);

    public static string Delta(string text, bool done) =>
        JsonSerializer.Serialize(new { op = "delta", text, done }, Options);

    public static string Error(string message) =>
        JsonSerializer.Serialize(new { op = "error", message }, Options);

    public static string Ok() =>
        JsonSerializer.Serialize(new { op = "ok" }, Options);

    public sealed record Envelope(string Op, string Text, bool Done, List<ChatMessage> Messages);

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long? Long(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;

    public static Envelope Parse(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var op = root.GetProperty("op").GetString() ?? "";
        var text = root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        var done = root.TryGetProperty("done", out var d) && d.GetBoolean();
        var messages = new List<ChatMessage>();
        if (root.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in arr.EnumerateArray())
            {
                messages.Add(new ChatMessage(
                    Str(m, "role"),
                    Str(m, "text"),
                    Long(m, "at") ?? Long(m, "atUnixMs") ?? 0));
            }
        }
        return new Envelope(op, text, done, messages);
    }
}

/// <summary>Conversation log with bounded size and local persistence.</summary>
public sealed class ChatLog
{
    public const int MaxMessages = 500;

    private static readonly JsonSerializerOptions PersistOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _gate = new();
    private readonly string? _filePath;
    private readonly List<ChatMessage> _messages = new();

    public ChatLog(string? filePath = null)
    {
        _filePath = filePath;
    }

    public IReadOnlyList<ChatMessage> Messages
    {
        get { lock (_gate) return _messages.ToList(); }
    }

    public void Add(string role, string text)
    {
        lock (_gate)
        {
            _messages.Add(new ChatMessage(role, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
            if (_messages.Count > MaxMessages)
            {
                _messages.RemoveRange(0, _messages.Count - MaxMessages);
            }
        }
        Persist();
    }

    public void Clear()
    {
        lock (_gate) _messages.Clear();
        Persist();
    }

    public void Load()
    {
        if (_filePath is null || !File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var messages = JsonSerializer.Deserialize<List<ChatMessage>>(json, PersistOptions);
            if (messages is not null)
            {
                lock (_gate) _messages.Clear();
                lock (_gate) _messages.AddRange(messages.TakeLast(MaxMessages));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // corrupt history degrades to empty
        }
    }

    private void Persist()
    {
        if (_filePath is null)
        {
            return;
        }

        try
        {
            List<ChatMessage> snapshot;
            lock (_gate) snapshot = _messages.ToList();
            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, PersistOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // persistence is best-effort
        }
    }
}
