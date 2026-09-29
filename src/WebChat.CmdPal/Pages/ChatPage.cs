// Copyright (c) Tai-Yng. MIT license.
//
// The in-palette chat page. The WebChat.App is the single source of truth: it persists
// the conversation and drives the DeepSeek page. This page polls the history over the
// local loopback API (fast while a reply is pending, slow while idle) and renders
// newest-first. Polling only updates item content — the list order is stable between
// polls, so the user's selection is never yanked (the Pomodoro lesson).

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
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan SlowPoll = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private readonly List<ChatMessage> _history = new();
    private string _query = string.Empty;
    private bool _online;          // App reachable on the last poll
    private bool _probedOnce;
    private bool _sending;         // a send was requested; waiting for the reply
    private DateTime _sendStartedAt;
    private string _pendingAutoSend = string.Empty; // queued while connecting; fires on connect
    private long _lastProbeTicks;
    private int _probing;
    private System.Threading.Timer? _pollTimer;

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
        bool online, probedOnce, sending;
        lock (_gate)
        {
            online = _online;
            probedOnce = _probedOnce;
            sending = _sending;
            history = new List<ChatMessage>(_history);
        }

        var items = new List<IListItem>();

        if (probedOnce && !online)
        {
            TryProbe(); // self-heal: re-check on every render, throttled
            items.Add(new ListItem(new LaunchAppCommand())
            {
                Title = "WebChat 未运行（自动重连中…）",
                Subtitle = "点这里启动窗口；在窗口里登录 DeepSeek 一次即可长期对话",
            });
            return items.ToArray();
        }

        // Pending turn: the App's history ends with an unanswered user message.
        var pending = sending && history.Count > 0 && history[^1].Role == ChatMessage.User;

        if (sending && !pending)
        {
            // Turn finished between polls; refresh once to pull the final history.
            TryProbe(force: true);
            return items.ToArray();
        }

        if (pending)
        {
            var waited = DateTime.UtcNow - _sendStartedAt;
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = waited > TurnTimeout ? "⏳ 回复超时（检查 WebChat 窗口是否已登录）" : "⏳ DeepSeek 回复中…",
                Subtitle = $"已等待 {waited.TotalSeconds:0}s · 完成后自动显示",
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

        if (history.Count > 0)
        {
            items.Add(new ListItem(new ClearChatCommand(this))
            {
                Title = "清空对话",
                Subtitle = "同时清空 App 内的历史记录",
            });
        }

        return items.ToArray();
    }

    /// <summary>One background poll of the App history; updates state and re-renders.</summary>
    private void TryProbe(bool force = false)
    {
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (!force && now - Volatile.Read(ref _lastProbeTicks) < 4000)
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
                bool turnJustCompleted;
                string? autoSend = null;
                lock (_gate)
                {
                    turnJustCompleted = _sending && history.Count > 0 && history[^1].Role == ChatMessage.Assistant;
                    _history.Clear();
                    _history.AddRange(history);
                    _online = true;
                    _probedOnce = true;
                    if (turnJustCompleted)
                    {
                        _sending = false;
                    }
                    if (_pendingAutoSend.Length > 0)
                    {
                        autoSend = _pendingAutoSend;
                        _pendingAutoSend = string.Empty;
                    }
                }
                if (autoSend is not null)
                {
                    BeginSend(autoSend); // App 上线，补发排队中的消息
                }
            }
            catch (Exception)
            {
                lock (_gate) _online = false;
            }
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
                RaiseItemsChanged(0);
            }
        });
    }

    internal SendResult BeginSend(string text)
    {
        lock (_gate)
        {
            // 卡住的发送自动放行：超过回合超时后允许重发。
            if (_sending && DateTime.UtcNow - _sendStartedAt > TurnTimeout + TimeSpan.FromSeconds(5))
            {
                _sending = false;
            }

            if (!_probedOnce || !_online)
            {
                // 初始探测未完成或 App 暂时离线：入队，连上后自动补发。
                _pendingAutoSend = text;
                return SendResult.Connecting;
            }

            if (_sending)
            {
                return SendResult.Busy;
            }

            _sending = true;
            _sendStartedAt = DateTime.UtcNow;
            _history.Add(new ChatMessage(ChatMessage.User, text, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ChatTcpClient.SendAsync(text, null, CancellationToken.None);
            }
            catch (Exception)
            {
                // the poll loop surfaces the failure state; the App also logs it
            }
        });

        StartFastPolling();
        RaiseItemsChanged(0);
        return SendResult.Started;
    }

    internal enum SendResult
    {
        Started,
        Connecting,
        Busy,
        Offline,
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

    /// <summary>Polls fast while a reply is pending, then drops to slow idle polling.</summary>
    private void StartFastPolling()
    {
        StopPollTimer();
        _pollTimer = new System.Threading.Timer(
            _ => TryProbe(force: true),
            null, FastPoll, FastPoll);
    }

    private void StopPollTimer()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
    }

    internal static string FirstLine(string text)
    {
        var line = text.Replace("\r", "").Split('\n')[0];
        return line.Length == 0 && text.Length > 0 ? "(多行内容)" : line;
    }

    internal static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    internal static string FormatTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime.ToString("HH:mm");
}

internal enum SendResult
{
    Started,
    Connecting,
    Busy,
    Offline,
}
