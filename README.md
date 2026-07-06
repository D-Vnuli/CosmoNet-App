# CosmoNet Windows App

Native Windows client for CosmoNet subscriptions. The first MVP is built on WPF and prepares a `sing-box` configuration from the existing `/sub/{sub_id}` subscription flow used by the Telegram bot.

## Current MVP

- Saves the user's subscription URL in `%AppData%\CosmoNet\settings.json`.
- Downloads and parses VLESS subscription links, including base64 encoded subscription bodies.
- Builds `%AppData%\CosmoNet\sing-box.json`.
- Starts and stops a bundled `sing-box.exe`.
- Supports TUN mode for full-device routing and mixed proxy fallback on `127.0.0.1:20808`.

## Local Development

```powershell
dotnet build D:\AI\MyProjectPlace\CosmoNet-App\CosmoNet.App.sln
dotnet run --project D:\AI\MyProjectPlace\CosmoNet-App\CosmoNet.App.csproj
```

For real VPN startup, place the Windows `sing-box.exe` binary here:

```text
D:\AI\MyProjectPlace\CosmoNet-App\Resources\sing-box\sing-box.exe
```

TUN mode requires running the app as administrator.
