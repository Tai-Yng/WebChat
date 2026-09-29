# WebChat

Chat with **DeepSeek web** in a dedicated desktop window — no API token needed. You log in once; the window keeps its own browser session (WebView2) independent of your main browser.

- **No API / no token**: renders the real chat.deepseek.com page in an embedded WebView2; your logged-in session persists in a dedicated profile
- **Command Palette ready**: ships with a PowerToys **Command Palette (CmdPal)** launcher extension — one command summons the window
- Small vertical window (420×640, resizable), **pin on top**, close-to-tray, single instance (second launch just wakes the window)
- Fully local: no telemetry, the only network traffic is DeepSeek itself

This repo contains two packaged apps:

| Project | What it is |
|---|---|
| `src/WebChat.App` | The chat window (WPF + WebView2), MSIX `TaiYng.WebChat.App` |
| `src/WebChat.CmdPal` | CmdPal launcher extension, MSIX `TaiYng.WebChat.CmdPal` |

## Install

1. Download both `.msix` files + `webchat-sign.cer` from Releases
2. Install the `.cer` into **Local Machine → Trusted People**
3. Install both `.msix` (double-click, or `Add-AppxPackage -Path <msix>`)
4. `Win+Alt+Space` → type **WebChat** → Enter; log in to DeepSeek once — you stay logged in

## Troubleshooting

- **Duplicate entries / broken icons after upgrading**: fully restart PowerToys (kill `Microsoft.CmdPal.UI.exe` too) or run "Reload" in Command Palette.
- **Login cleared**: the session lives in `%LOCALAPPDATA%\Packages\TaiYng.WebChat.App.../LocalState` — don't delete the app via "Reset" in Windows settings.

## Development

```powershell
dotnet build src/WebChat.App/WebChat.App.csproj -p:Platform=x64
dotnet build src/WebChat.CmdPal/WebChat.CmdPal.csproj -p:Platform=x64
dotnet test tests/WebChat.Tests/WebChat.Tests.csproj
```

v2 backlog: custom chat UI (DOM adapter, no web skin), multi-site (adapter interface reserved), global hotkey, message export.

## License

MIT
