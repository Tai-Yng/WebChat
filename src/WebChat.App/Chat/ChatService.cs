// Copyright (c) Tai-Yng. MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebChat.Protocol;

namespace WebChat.App;

/// <summary>
/// Orchestrates a conversation turn: send via the DOM bridge, poll the page for the
/// streaming answer, finalize into the log. Reports progressive (fullTextSoFar, done)
/// through the callback given to SendAsync.
/// </summary>
public sealed class ChatService
{
    private const int PollMs = 400;
    private static readonly TimeSpan SendTimeout = TimeSpan.FromMinutes(4);

    private readonly DeepSeekBridge _bridge;
    private readonly ChatLog _log;
    private Action<string, bool>? _currentDelta;
    private int _busy;

    public ChatService(DeepSeekBridge bridge, ChatLog log)
    {
        _bridge = bridge;
        _log = log;
        _log.Load();
    }

    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public IReadOnlyList<ChatMessage> Messages => _log.Messages;

    public void Clear() => _log.Clear();

    /// <summary>Runs one turn to completion. Returns the final assistant text.</summary>
    public async Task<string> SendAsync(string text, Action<string, bool>? delta, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            throw new InvalidOperationException("busy");
        }

        _currentDelta = delta;
        try
        {
            _log.Add(ChatMessage.User, text);
            BootLog.Instance.Step("chat: page-ready wait");

            if (!await WaitPageReadyAsync(ct))
            {
                return Fail("DeepSeek 页面未就绪（请先打开 WebChat 窗口并登录）");
            }

            BootLog.Instance.Step("chat: pre-send");
            if (!await _bridge.SendAsync(text))
            {
                var hint = "请打开 WebChat 窗口登录 DeepSeek 后再试（登录一次长期保持；若已登录则是页面改版，需更新适配器）";
                return Fail(hint);
            }
            BootLog.Instance.Step("chat: sent, polling");

            var started = DateTimeOffset.UtcNow;
            var last = string.Empty;
            var stable = 0;
            var sawContent = false;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - started > SendTimeout)
                {
                    return Fail("等待回复超时");
                }

                await Task.Delay(PollMs, ct);
                // Poll via InspectAsync — its data path is proven reliable end-to-end.
                System.Text.Json.JsonDocument? snapshot = null;
                try
                {
                    snapshot = System.Text.Json.JsonDocument.Parse(await _bridge.InspectAsync());
                }
                catch (System.Text.Json.JsonException)
                {
                    continue;
                }

                using (snapshot)
                {
                    var root = snapshot.RootElement;
                    var current = root.TryGetProperty("lastMdText", out var t) ? t.GetString() ?? "" : "";
                    var generatingRaw = root.TryGetProperty("generatingBtn", out var g) ? g.GetString() ?? "" : "";
                    var generating = !generatingRaw.Contains("ds-button--disabled");

                    if (current.Length > 0)
                    {
                        sawContent = true;
                    }

                    if (current.Length != last.Length)
                    {
                        last = current;
                        stable = 0;
                        delta?.Invoke(current, false);
                    }
                    else
                    {
                        stable++;
                    }

                    // 完成判定以文本稳定性为准（2026-09-29 改版后发送按钮不再带
                    // ds-button--disabled，generating 信号不可依赖）。
                    if (sawContent && stable >= 5)
                    {
                        _log.Add(ChatMessage.Assistant, last);
                        delta?.Invoke(last, true);
                        return last;
                    }
                }
                {
                    _log.Add(ChatMessage.Assistant, last);
                    delta?.Invoke(last, true);
                    return last;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return "[error] 已取消";
        }
        catch (Exception ex)
        {
            return Fail($"对话错误: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private string Fail(string message)
    {
        _log.Add(ChatMessage.Assistant, $"[error] {message}");
        BootLog.Instance.Step("send failed: " + message);
        try { _currentDelta?.Invoke($"[error] {message}", true); } catch { }
        return $"[error] {message}";
    }

    private async Task<bool> WaitPageReadyAsync(CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await _bridge.EnsureInjectedAsync())
            {
                return true;
            }
            await Task.Delay(200, ct);
        }
        return false;
    }
}
