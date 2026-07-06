# CosmoNet Windows App

Native Windows client for CosmoNet subscriptions. The app is built on WPF and prepares a `sing-box` configuration from the existing `/sub/{sub_id}` subscription flow used by the Telegram bot.

## Current Stage

Stage 5: account/session model, subscription display, local security, split tunneling, and sing-box diagnostics.

Implemented:

- Main screen with fixed subscription expiry, subscription status, server status, and connection state blocks.
- One-click power button: gray means disconnected, blue means connecting, green means connected.
- Telegram authorization UX placeholder with a short login code flow for the future bot confirmation API.
- Account/session and subscription summary models for tariff, expiry date, device limit, traffic usage, and sync state.
- Collapsible subscription details so the main screen stays compact.
- Separate tabs for the main dashboard, app routing, and instructions.
- Traffic mode selection: all traffic through VPN or only selected applications.
- Local discovery of running/installed `.exe` applications for split tunneling selection.
- Manual process entry for apps that are not found automatically, for example `discord.exe` or `chrome.exe`.
- Lazy app loading: saved selected processes appear instantly, full app scan runs only when requested.
- `sing-box` route generation for selected applications using `process_name` rules.
- A diagnostics tab for checking core availability, admin rights, generated config path, protected storage path, and `sing-box check -c` output.
- Connection startup validates the generated config with `sing-box check -c` before running the VPN core.
- Subscription URL is stored outside public settings in a Windows DPAPI-protected local file.
- Public settings stay in `%AppData%\CosmoNet\settings.json`; protected secrets stay in `%AppData%\CosmoNet\secrets.dat`.

Not implemented yet:

- Real Telegram backend confirmation and secure account token exchange.
- Real subscription metadata API for expiry date and user plan details.
- Bundled installer and automatic `sing-box` core delivery.
- End-to-end runtime validation with a real CosmoNet server profile.
- In-app live traffic counters and process-level routing verification.

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
- Subscription URLs and future auth tokens must live in the DPAPI-protected `secrets.dat`, not in `settings.json`.
- The app stores selected process names locally; absolute app paths are only used for local display.
- The `sing-box` binary is intentionally ignored by git and should be supplied by the installer or local developer setup.


