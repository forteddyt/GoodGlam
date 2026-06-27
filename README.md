# GoodGlam

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
5. If a glamour using the item has **≥ the configured loves threshold** (default **100**), a
   native toast notification fires.

Results are cached per item to stay polite to Eorzea Collection.

### A note on the data transport (important)

Eorzea Collection has **no public API**, and its Cloudflare WAF **blocks .NET's managed HTTP
stacks** (`SocketsHttpHandler` *and* `WinHttpHandler`) via TLS fingerprinting — they get a `403`
even with browser-like headers. The system **`curl.exe`** (shipped with Windows 10 1803+) produces
a TLS handshake Cloudflare accepts, so the MVP shells out to it.

This is a pragmatic MVP choice with known trade-offs (a child process spawned from the game looks
unusual to AV/EDR, and would not be accepted into the official Dalamud plugin repository). The
planned replacement is a **GitHub Actions** crawler that builds a static JSON index which the
plugin downloads in-process — see [`docs/STATUS.md`](docs/STATUS.md#planned-replacement-deferred-preferred).

## Building

Requirements: **.NET 10 SDK** (Windows).

The project references the Dalamud dev libraries directly. Restore them once into the repo-root
`.dalamud/` folder:

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

### Loading in-game (dev)

In Dalamud's `/xlsettings` → *Experimental* → *Dev Plugin Locations*, add the path to the built
`GoodGlam.dll`, then enable **GoodGlam** in the plugin installer.

## Usage

- `/goodglam` — open the settings window.
- Settings: enable/disable notifications, the loves threshold, and the cache lifetime.

## Roadmap

- **Annotate the Need/Greed window** itself (mark popular items inline) instead of a toast.
- **Replace the curl subprocess** with a GitHub Actions crawler + static JSON index.
- **Configurable filters** mirroring Eorzea Collection (gender, date, tags, intended-for/job,
  level to equip).
- **Eorzea Collection login** to restrict notifications to glamours you've saved.

## Project layout

```
src/GoodGlam/
  Plugin.cs                      plugin entry + wiring
  Services.cs                    Dalamud service locator
  Configuration.cs               persisted settings
  Glam/GlamSlot.cs               EquipSlotCategory -> EC slot mapping
  Glam/ItemResolver.cs           game item ID -> name + slot (Lumina)
  Glam/EorzeaCollectionClient.cs IGlamSource (curl-backed EC client)
  Glam/GlamPopularityService.cs  orchestration + caching + notify
  Loot/LootWatcher.cs            NeedGreed addon hook + Loot struct read
  Windows/ConfigWindow.cs        settings UI
```
