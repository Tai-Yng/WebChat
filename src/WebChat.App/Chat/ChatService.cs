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

        try
        {
            _log.Add(ChatMessage.User, text);

            if (!await WaitPageReadyAsync(ct))
            {
                return Fail("DeepSeek 页面未就绪（请先打开 WebChat 窗口并登录）");
            }

            var baseline = await _bridge.AnswerCountAsync();
            if (baseline < 0 || !await _bridge.SendAsync(text))
            {
                return Fail("发送失败（输入框未找到或页面结构变更）");
            }

            var started = DateTimeOffset.UtcNow;
            var last = string.Empty;
            var stable = 0;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - started > SendTimeout)
                {
                    return Fail("等待回复超时");
                }

                await Task.Delay(PollMs, ct);
                var count = await _bridge.AnswerCountAsync();
                var current = count > baseline ? await _bridge.LastAnswerAsync() : string.Empty;
                var generating = await _bridge.IsGeneratingAsync();

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

                if (count > baseline && !generating && stable >= 2)
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
