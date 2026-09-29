// Copyright (c) Tai-Yng. MIT license.
//
// The in-palette chat page. Conversation is stored in WebChat.App; this page talks to it
// over the local pipe. Items render newest-first so the streaming answer sits at index 0
// and periodic RaiseItemsChanged updates it in place without yanking the selection
// (the Pomodoro lesson: never let a rebuild move the user's selection).

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using WebChat.CmdPal.Commands;
using WebChat.Protocol;

namespace WebChat.CmdPal;

public sealed partial class ChatPage : DynamicListPage
{
    private static readonly TimeSpan RefreshTick = TimeSpan.FromMilliseconds(800);

    private readonly object _gate = new();
    private readonly List<ChatMessage> _history = new();
    private string _query = string.Empty;
    private bool _pipeProbed;
    private bool _pipeOk;
    private bool _generating;
    private string _stream = string.Empty;
    private long _lastProbeTicks;
    private int _probing;
    private System.Threading.Timer? _refreshTimer;

    public ChatPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\icon.png");
        Title = "WebChat";
        Name = "Open";
        PlaceholderText = "输入要发送给 DeepSeek 的消息…";
        ShowDetails = true;

        TryProbe();
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        _query = newSearch;
        RaiseItemsChanged(0);
    }

    public override IListItem[] GetItems()
    {
        List<ChatMessage> history;
        bool pipeOk, generating, loaded;
        string stream;
        lock (_gate)
        {
            history = new List<ChatMessage>(_history);
            pipeOk = _pipeOk;
            generating = _generating;
            loaded = _pipeProbed;
            stream = _stream;
        }

        var items = new List<IListItem>();

        if (loaded && !pipeOk)
        {
            TryProbe(); // self-heal: the App may have started since the last attempt
            items.Add(new ListItem(new LaunchAppCommand())
            {
                Title = "WebChat 未运行（正在自动重连…）",
                Subtitle = "点这里手动启动窗口；窗口登录 DeepSeek 一次即可长期对话",
            });
            return items.ToArray();
        }

        // Streaming answer lives at index 0 so in-place refresh never moves the selection.
        if (generating)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "⏳ 回复中…",
                Subtitle = Preview(stream),
                Details = new Details { Title = "DeepSeek", Body = stream.Length > 0 ? stream : "_生成中_" },
            });
        }
        else if (!string.IsNullOrWhiteSpace(_query))
        {
            items.Add(new ListItem(new SendChatCommand(this, _query.Trim()))
            {
                Title = $"发送: {SendChatCommand.Truncate(_query.Trim(), 60)}",
                Subtitle = "Enter 发送到 DeepSeek",
            });
        }

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var m = history[i];
            var isUser = m.Role == ChatMessage.User;
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = $"{(isUser ? "🧑" : "🤖")} {SendChatCommand.Truncate(FirstLine(m.Text), 70)}",
                Subtitle = isUser ? "你" : $"DeepSeek · {FormatTime(m.AtUnixMs)}",
                Details = new Details
                {
                    Title = isUser ? "你" : "DeepSeek",
                    Body = m.Text,
                },
            });
        }

        if (!generating && (history.Count > 0 || string.IsNullOrWhiteSpace(_query)))
        {
            items.Add(new ListItem(new ClearChatCommand(this))
            {
                Title = "清空对话",
                Subtitle = loaded && pipeOk ? "同时清空 App 内的历史记录" : "WebChat 未运行",
            });
        }

        return items.ToArray();
    }

    /// <summary>Re-asks the App for history when offline; throttled to one attempt per 5s.</summary>
    private void TryProbe()
    {
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastProbeTicks) < 5000)
        {
            Interlocked.Exchange(ref _probing, 0);
            return;
        }
        Volatile.Write(ref _lastProbeTicks, now);

        _ = Task.Run(async () =>
        {
            try
            {
                var history = await ChatTcpClient.HistoryAsync(CancellationToken.None);
                lock (_gate)
                {
                    _history.Clear();
                    _history.AddRange(history);
                    _pipeOk = true;
                    _pipeProbed = true;
                }
                ProbeLog("ok, history=" + history.Count);
            }
            catch (Exception ex)
            {
                lock (_gate) _pipeOk = false;
                ProbeLog("FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
                RaiseItemsChanged(0);
            }
        });
    }

    /// <summary>Probe diagnostics land in the package's LocalState so they are readable from outside.</summary>
    private static void ProbeLog(string message)
    {
        try
        {
            var dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "probe.log"),
                $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
        catch
        {
            // diagnostics are best-effort
        }
    }

    internal bool BeginSend(string text)
    {
        lock (_gate)
        {
            if (_generating || !_pipeOk)
            {
                return false;
            }
            _generating = true;
            _stream = string.Empty;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ChatTcpClient.SendAsync(text, (full, done) =>
                {
                    lock (_gate) _stream = full;
                    if (done)
                    {
                        StopTimer();
                    }
                }, CancellationToken.None);

                // Pull the finalized history (user + assistant entries) from the App.
                var history = await ChatTcpClient.HistoryAsync(CancellationToken.None);
                lock (_gate)
                {
                    _history.Clear();
                    _history.AddRange(history);
                    _generating = false;
                }
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    _generating = false;
                    _pipeOk = false;
                }
            }
            finally
            {
                StopTimer();
                RaiseItemsChanged(0);
            }
        });

        // Refresh the streaming item while the answer grows.
        _refreshTimer = new System.Threading.Timer(_ => RaiseItemsChanged(0), null, RefreshTick, RefreshTick);
        RaiseItemsChanged(0);
        return true;
    }

    internal bool BeginClear()
    {
        try
        {
            _ = Task.Run(async () =>
            {
                await ChatTcpClient.ClearAsync(CancellationToken.None);
                lock (_gate) _history.Clear();
                RaiseItemsChanged(0);
            });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void StopTimer()
    {
        _refreshTimer?.Dispose();
        _refreshTimer = null;
    }

    internal static string FirstLine(string text)
    {
        var line = text.Replace("\r", "").Split('\n')[0];
        return line.Length == 0 && text.Length > 0 ? "(多行内容)" : line;
    }

    internal static string Preview(string text) =>
        text.Length == 0 ? "等待 DeepSeek 响应…" : SendChatCommand.Truncate(FirstLine(text), 80);

    internal static string FormatTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("HH:mm");
}
