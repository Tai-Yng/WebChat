// Copyright (c) Tai-Yng. MIT license.
//
// Injected into the DeepSeek page. DOM dialect sourced from ChatDeck/adapters/deepseek.js
// (2026-09 探查结论): answers are .ds-message with a direct .ds-markdown child, generating
// state read from the bottom circle button's ds-button--disabled marker, input is
// textarea#chat-input. If DeepSeek ships a redesign, start here.

using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebChat.App;

public sealed class DeepSeekBridge
{
    private static readonly string AdapterScript = """
        (() => {
          if (window.__wc) return JSON.stringify({ok: true});
          const contentOf = el => (el && el.querySelector(':scope > .ds-markdown') || el).innerText;
          const circleButtons = () => Array.from(document.querySelectorAll('[role="button"]')).filter(b => {
            const c = typeof b.className === 'string' ? b.className : '';
            if (!c.includes('ds-button--circle')) return false;
            const r = b.getBoundingClientRect();
            return r.y > 400 && r.width > 0;
          });
          const enabledCircle = () => circleButtons().find(b =>
            !(typeof b.className === 'string' && b.className.includes('ds-button--disabled')));
          window.__wc = {
            count: () => document.querySelectorAll('.ds-message:has(> .ds-markdown)').length,
            // 2026-09-29：发送后页面会跳转会话页并重建 DOM，计数基线会错位；
            // 改为直接取最后一个 markdown 块（流式期间它就是正在生长的回复）。
            last: () => {
              const mds = document.querySelectorAll('.ds-markdown');
              return mds.length ? mds[mds.length - 1].innerText : '';
            },
            generating: () => {
              const btn = enabledCircle();
              return !!btn;
            },
            inspect: () => {
              const allMd = document.querySelectorAll('[class*="markdown"]');
              const lastMd = allMd.length ? allMd[allMd.length - 1] : null;
              return JSON.stringify({
                url: location.href.slice(-50),
                dsMarkdown: document.querySelectorAll('.ds-markdown').length,
                dsMessage: document.querySelectorAll('.ds-message').length,
                anyMarkdown: allMd.length,
                lastMdClass: lastMd ? String(lastMd.className).slice(0, 140) : '',
                lastMdText: lastMd ? String(lastMd.innerText || '').slice(0, 200) : '',
                generatingBtn: (() => { const b = Array.from(document.querySelectorAll('[role="button"]')).find(x => String(x.className).includes('ds-button--circle')); return b ? String(b.className).slice(0, 100) : 'none'; })(),
              });
            },
            send: (text) => {
              // 2026-09-29 改版探查：输入框已无 #chat-input，是无 id 的 textarea
              // （placeholder 含"发送消息"）。选择器按此定位并对旧 id 兜底。
              const ta = document.querySelector('#chat-input')
                || Array.from(document.querySelectorAll('textarea')).find(t =>
                  (t.placeholder || '').includes('发送消息'))
                || document.querySelector('textarea');
              if (!ta) return location.pathname.includes('sign_in') ? 'not-logged-in' : 'no-input';
              ta.focus();
              const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')?.set;
              if (setter) {
                setter.call(ta, text);
                ta.dispatchEvent(new Event('input', { bubbles: true }));
              } else {
                const sel = window.getSelection();
                sel.removeAllRanges();
                const range = document.createRange();
                range.selectNodeContents(ta);
                range.collapse(false);
                sel.addRange(range);
                document.execCommand('insertText', false, text);
              }
              const btn = enabledCircle();
              if (btn) { btn.click(); return 'sent'; }
              // 改版后发送按钮可能不再匹配 ds-button--circle：按 Enter 兜底
              const opts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true };
              ta.dispatchEvent(new KeyboardEvent('keydown', opts));
              ta.dispatchEvent(new KeyboardEvent('keyup', opts));
              return 'sent-enter';
            },
          };
          return JSON.stringify({ok: true});
        })()
        """;

    private readonly Microsoft.Web.WebView2.Wpf.WebView2 _web;
    private readonly System.Windows.Threading.Dispatcher _dispatcher;

    public DeepSeekBridge(Microsoft.Web.WebView2.Wpf.WebView2 web)
    {
        _web = web;
        _dispatcher = web.Dispatcher;
    }

    public bool PageReady =>
        _web.CoreWebView2 is not null
        && _web.Source is not null
        && _web.Source.Host.EndsWith("deepseek.com", StringComparison.OrdinalIgnoreCase);

    public async Task<bool> EnsureInjectedAsync()
    {
        if (!PageReady)
        {
            return false;
        }

        var result = await Eval(AdapterScript);
        var ok = result.Contains("\"ok\"");
        if (!ok)
        {
            BootLog.Instance.Step("adapter inject -> " + (result.Length > 80 ? result[..80] : result));
        }
        return ok;
    }

    public async Task<int> AnswerCountAsync()
    {
        if (!await EnsureInjectedAsync())
        {
            return -1;
        }
        return await EvalInt("window.__wc.count()");
    }

    public async Task<string> LastAnswerAsync()
    {
        if (!await EnsureInjectedAsync())
        {
            return string.Empty;
        }
        return await EvalString("window.__wc.last()");
    }

    public async Task<bool> IsGeneratingAsync()
    {
        if (!await EnsureInjectedAsync())
        {
            return false;
        }
        return await EvalInt("window.__wc.generating() ? 1 : 0") == 1;
    }

    /// <summary>Types the message into the page's input and clicks send.</summary>
    public async Task<bool> SendAsync(string text)
    {
        if (!await EnsureInjectedAsync())
        {
            return false;
        }

        var encoded = JsonSerializer.Serialize(text);
        var result = await Eval($"window.__wc.send({encoded})");
        BootLog.Instance.Step("send() -> " + (result.Length > 60 ? result[..60] : result));
        return result.Contains("sent");
    }

    /// <summary>Dumps candidate input elements so selector drift can be diagnosed remotely.</summary>
    public async Task<string> InspectAsync()
    {
        if (!await EnsureInjectedAsync())
        {
            return "not-ready";
        }
        return await Eval("window.__wc.inspect()");
    }

    /// <summary>Current page path (for login-state hints).</summary>
    public async Task<string> CurrentPathAsync()
    {
        try
        {
            return await Eval("location.pathname");
        }
        catch
        {
            return string.Empty;
        }
    }

    private async Task<string> Eval(string script)
    {
        // ExecuteScriptAsync must run on the WebView2's UI thread; callers poll from
        // background threads (pipe server / chat polling loop).
        var json = await _dispatcher.InvokeAsync(
            () => _web.CoreWebView2.ExecuteScriptAsync(script)).Task.Unwrap();

        // The result is the JSON encoding of the script's completion value. When that
        // value is a string, unwrap it; otherwise pass the raw JSON through (numbers
        // from count()/generating() etc.). Never assume the shape.
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String
                ? doc.RootElement.GetString() ?? string.Empty
                : json;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private async Task<int> EvalInt(string script) =>
        int.TryParse(await Eval(script), out var n) ? n : 0;

    private async Task<string> EvalString(string script)
    {
        var json = await Eval(script);
        try
        {
            return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
