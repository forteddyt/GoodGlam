<p align="center">
  <img src="src/GoodGlam/Assets/Logo.svg" alt="GoodGlam logo" width="120" height="120" />
</p>

# GoodGlam

[![CI](https://github.com/forteddyt/GoodGlam/actions/workflows/ci.yml/badge.svg)](https://github.com/forteddyt/GoodGlam/actions/workflows/ci.yml)
[![Coverage](https://forteddyt.github.io/GoodGlam/badge_linecoverage.svg)](https://forteddyt.github.io/GoodGlam/)

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **FINAL FANTASY XIV** that watches
the **Need/Greed roll window** and notifies you when a rollable item is used in a *popular*
glamour on [Eorzea Collection](https://ffxiv.eorzeacollection.com/glamours) — so you can decide
whether an unassuming dungeon/trial/raid drop is actually worth rolling Need on.

> ⚠️ Third-party tools violate Square Enix's Terms of Service. Use at your own risk.

> 📋 **Resuming work / picking up on another machine?** See [`docs/STATUS.md`](docs/STATUS.md)
> for current state, what's verified vs. untested, and next steps.

## How it works

1. When the `NeedGreed` addon appears, the plugin reads each rollable item ID straight from the
   game's `Loot` struct (via FFXIVClientStructs) — no packet capture.
2. Each item is resolved to a name + equipment slot from the game's data sheets (Lumina).
3. The game item ID is bridged to Eorzea Collection's own item ID via EC's
   `POST /gear/<slot>/search` endpoint (matching on the `XIVApiId` field).
4. EC's glamour listing is queried sorted by loves (`GET /glamours?...&filter[orderBy]=loves`),
   and the top glamour's love count is parsed.
5. If a glamour using the item has **≥ the configured loves threshold** (default **100**), the drop
   is recorded in a **persistent, browsable history window** and the floating GoodGlam logo lights
   up with a **pulsing golden glow** (click it to open the history; the glow then clears).

Results are cached per item to stay polite to Eorzea Collection.

> 💡 The old transient toast and bell have been replaced: qualifying drops are now logged to a
> **scrollable, persistent history window** (each row has a clickable glamour link), and the
> floating logo glows gold until you open the history, so a popular drop can always be pulled back
> up — even after you step away.

### A note on the data transport (important)

Eorzea Collection has **no public API**, and on **native Windows** its Cloudflare WAF **blocks
.NET's managed HTTP stacks** (`SocketsHttpHandler` *and* `WinHttpHandler`) via TLS fingerprinting —
they get a `403` even with browser-like headers. The system **`curl.exe`** (shipped with Windows 10
1803+) produces a TLS handshake Cloudflare accepts, so on Windows the plugin shells out to it.

**On Linux (XIVLauncher.Core / Wine)** the situation is the opposite: there is no `curl.exe` in the
Wine prefix (and launching a native `curl` via `Process.Start` fails), but .NET's in-process
`HttpClient` *does* reach Eorzea Collection — Wine's TLS goes through GnuTLS/OpenSSL, a fingerprint
Cloudflare accepts. So rather than sniff the OS, the plugin **tries in-process `HttpClient` first and
falls back to the `curl.exe` subprocess only if managed HTTP comes back blocked**, remembering which
one worked. This is self-correcting: it uses curl only on native Windows where it's needed, and drops
it automatically if Cloudflare ever stops blocking. The transport seam lives in `Glam/EcTransport.cs`.

The `curl.exe` subprocess is a pragmatic Windows-only choice with known trade-offs (a child process
spawned from the game looks unusual to AV/EDR, and would not be accepted into the official Dalamud
plugin repository). The planned replacement is a **GitHub Actions** crawler that builds a static JSON
index which the plugin downloads in-process — see
[`docs/STATUS.md`](docs/STATUS.md#planned-replacement-deferred-preferred).

## Building

Requirements: **.NET 10 SDK** (Windows **or** Linux — the plugin builds and runs on both).

The project references the Dalamud dev libraries directly. On Windows, restore them once into the
repo-root `.dalamud/` folder:

```powershell
# from the repo root
New-Item -ItemType Directory -Force -Path .dalamud | Out-Null
curl.exe -L -o .dalamud/latest.zip https://goatcorp.github.io/dalamud-distrib/latest.zip
Expand-Archive -Path .dalamud/latest.zip -DestinationPath .dalamud -Force
```

Then build:

```powershell
dotnet build src/GoodGlam/GoodGlam.csproj -c Release
```

The plugin (`GoodGlam.dll`) and manifest (`GoodGlam.json`) are emitted to
`src/GoodGlam/bin/Release/`.

> Prefer your own XIVLauncher dev hooks? Set the `DALAMUD_HOME` environment variable to that folder
> and it will be used instead of `.dalamud/`.

### Building on Linux (XIVLauncher.Core)

You don't need Windows. Build against the same Dalamud dev libraries XIVLauncher.Core already keeps
on disk by pointing `DALAMUD_HOME` at them:

```bash
export DALAMUD_HOME="$HOME/.xlcore/dalamud/Hooks/dev"
dotnet build src/GoodGlam/GoodGlam.csproj -c Release
```

Then add the **Linux path** to the built `GoodGlam.dll` under `/xlsettings` → *Experimental* →
*Dev Plugin Locations* (XIVLauncher.Core maps your home directory into the Wine prefix, so the
native path works directly).

### Loading in-game (dev)

In Dalamud's `/xlsettings` → *Experimental* → *Dev Plugin Locations*, add the path to the built
`GoodGlam.dll`, then enable **GoodGlam** in the plugin installer.

## Usage

- `/goodglam` — open the **history** window (browsable, persistent list of popular drops).
- `/goodglam config` — open the settings window (also has an **Open history** button).
- Each history row shows the timestamp, item name, top loves count, and a **clickable** glamour
  name that opens the Eorzea Collection page. History persists across game sessions; **Clear**
  empties it.
- Qualifying drops make the floating logo glow gold — click it to open the history (the glow clears).
- Settings: enable/disable notifications, the loves threshold, the cache lifetime, and filters.

## Roadmap

- **Annotate the Need/Greed window** itself (mark popular items inline, top hearts count).
- **Replace the curl subprocess** with a GitHub Actions crawler + static JSON index.
- **Eorzea Collection login** to restrict notifications to glamours you've saved.

## Project layout

```
src/GoodGlam/
  Plugin.cs                      plugin entry + wiring
  Services.cs                    Dalamud service locator
  Configuration.cs               persisted settings
  Glam/GlamSlot.cs               EquipSlotCategory -> EC slot mapping
  Glam/ItemResolver.cs           game item ID -> name + slot (Lumina)
  Glam/EcTransport.cs            managed-first HttpClient w/ curl.exe fallback
  Glam/EorzeaCollectionClient.cs IGlamSource (EC request building + parsing)
  Glam/GlamPopularityService.cs  orchestration + caching + notify
  History/NotificationHistory.cs persistent drop history store (JSON, capped)
  History/HistoryNotifier.cs     records drops + raises the logo glow signal
  History/NotificationState.cs   shared "unseen popular drop" flag (drives the logo glow)
  Loot/LootWatcher.cs            NeedGreed addon hook + Loot struct read
  Windows/ConfigWindow.cs        settings UI
  Windows/HistoryWindow.cs       scrollable popular-drop history
```

> ℹ️ This plugin was developed with AI assistance.
