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
            last: () => {
              const els = document.querySelectorAll('.ds-message:has(> .ds-markdown)');
              return els.length ? contentOf(els[els.length - 1]) : '';
            },
            generating: () => {
              const btn = enabledCircle();
              return !!btn;
            },
            send: (text) => {
              const ta = document.querySelector('#chat-input');
              if (!ta) return 'no-input';
              ta.focus();
              if (ta instanceof HTMLTextAreaElement) {
                const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set;
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
              if (!btn) return 'no-button';
              btn.click();
              return 'sent';
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
        return result.Contains("\"ok\"");
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
        return result.Contains("sent");
    }

    private async Task<string> Eval(string script)
    {
        // ExecuteScriptAsync must run on the WebView2's UI thread; callers poll from
        // background threads (pipe server / chat polling loop).
        var json = await _dispatcher.InvokeAsync(
            () => _web.CoreWebView2.ExecuteScriptAsync(script)).Task.Unwrap();
        return JsonSerializer.Deserialize<string>(json) ?? string.Empty;
    }

    private async Task<int> EvalInt(string script) =>
        int.TryParse(await Eval(script), out var n) ? n : 0;

    private async Task<string> EvalString(string script)
    {
        var json = await _web.CoreWebView2.ExecuteScriptAsync(script);
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
