# CosmoNet Windows App

Native Windows client for CosmoNet subscriptions. The app is built on WPF and prepares a `sing-box` configuration from the existing `/sub/{sub_id}` subscription flow used by the Telegram bot.

## Current Stage

Stage 2: product UX skeleton and routing foundation.

Implemented:

- Main screen with fixed subscription expiry, server status, and connection state blocks.
- One-click power button: gray means disconnected, blue means connecting, green means connected.
- Telegram authorization UX placeholder with a short login code flow for the future bot confirmation API.
- Collapsible subscription details so the main screen stays compact.
- Separate tabs for the main dashboard, app routing, and instructions.
- Traffic mode selection: all traffic through VPN or only selected applications.
- Local discovery of running/installed `.exe` applications for split tunneling selection.
- `sing-box` route generation for selected applications using `process_name` rules.
- Local-only settings in `%AppData%\CosmoNet\settings.json`.

Not implemented yet:

- Real Telegram backend confirmation and secure account token exchange.
- Real subscription metadata API for expiry date and user plan details.
- Bundled installer and automatic `sing-box` core delivery.
- End-to-end runtime validation with a real CosmoNet server profile.

## Local Development

```powershell
dotnet build D:\AI\MyProjectPlace\CosmoNet-App\CosmoNet.App.sln
dotnet run --project D:\AI\MyProjectPlace\CosmoNet-App\CosmoNet.App.csproj
```

After a debug build, the executable is here:

```text
D:\AI\MyProjectPlace\CosmoNet-App\bin\Debug\net9.0-windows\CosmoNet.App.exe
```

For real VPN startup, place the Windows `sing-box.exe` binary here:

```text
D:\AI\MyProjectPlace\CosmoNet-App\Resources\sing-box\sing-box.exe
```

TUN mode requires running the app as administrator.

## Security Notes

- Do not commit `.env`, subscription URLs, Telegram identifiers, generated configs, or local settings.
- The app stores selected process names locally; absolute app paths are only used for local display.
- The `sing-box` binary is intentionally ignored by git and should be supplied by the installer or local developer setup.
