# Project status & handoff

_Last updated: 2026-06-26_

This document captures the current state of GoodGlam so work can resume on another machine.
For end-user build/run instructions see the [README](../README.md); this file focuses on the
**development state, key decisions, and what's next**.

## TL;DR

- **What it is:** a Dalamud (C#) plugin that watches the Need/Greed roll window and fires a toast
  when a rollable item is used in a *popular* glamour on Eorzea Collection (EC).
- **State:** MVP **feature-complete and compiles clean** (0 warnings / 0 errors) against
  **Dalamud API 15 / `net10.0-windows`**. EC integration logic is **runtime-verified** against the
  live site. **Not yet tested inside the running game.**
- **Biggest gotcha:** EC blocks .NET's HTTP stacks via Cloudflare TLS fingerprinting. The MVP works
  around this by shelling out to the system **`curl.exe`** (see [Transport](#transport-important)).

## Quick start on a fresh machine

Requires the **.NET 10 SDK** on **Windows** (the plugin is Windows-only; it also relies on
`C:\Windows\System32\curl.exe`, present on Windows 10 1803+).

```powershell
# 1. Restore the Dalamud dev libraries into .dalamud/ (gitignored, ~56 MB)
New-Item -ItemType Directory -Force -Path .dalamud | Out-Null
curl.exe -L -o .dalamud/latest.zip https://goatcorp.github.io/dalamud-distrib/latest.zip
Expand-Archive -Path .dalamud/latest.zip -DestinationPath .dalamud -Force

# 2. Build
dotnet build src/GoodGlam/GoodGlam.csproj -c Release
# -> src/GoodGlam/bin/Release/GoodGlam.dll  (+ GoodGlam.json)
```

> The csproj defaults to the repo-local `.dalamud/` folder. To use your own XIVLauncher dev hooks
> instead, set the `DALAMUD_HOME` environment variable to that folder.

### Load in-game (dev)

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add the path to the built
   `GoodGlam.dll`.
2. Open the plugin installer, enable **GoodGlam**.
3. `/goodglam` opens settings. Enter a duty, win/contest a roll, and confirm a toast appears for
   glam-worthy drops.

## What works / what's verified

| Area | Status |
|---|---|
| Project builds (API 15, net10.0-windows) | ✅ 0 warn / 0 err |
| EC item search `POST /gear/<slot>/search` (game ID → EC ID) | ✅ runtime-verified (25430 → 14930) |
| EC popularity scrape `GET /glamours?...orderBy=loves` (parse loves) | ✅ runtime-verified |
| `curl.exe` transport from .NET subprocess | ✅ verified |
| `NeedGreed` addon hook + `Loot` struct read | ⛔ not tested in-game |
| Toast notification on qualifying drop | ⛔ not tested in-game |
| Config window / `/goodglam` command | ⛔ not tested in-game |

**Next concrete step:** load the DLL in a live client and confirm the `NeedGreed` hook fires and a
toast renders. Everything up to the EC calls is verified; the in-game UI/hook path is the untested
remainder.

## Transport (important)

EC has **no public API**, and its Cloudflare WAF **hard-blocks .NET's managed HTTP** — both
`SocketsHttpHandler` and `WinHttpHandler` return `403` even with full browser headers, because
Cloudflare fingerprints the TLS ClientHello (JA3/JA4). The system **`curl.exe`** (libcurl/Schannel)
produces a handshake Cloudflare accepts, so the MVP shells out to it via `System.Diagnostics.Process`.

- Dalamud does **not** sandbox plugins, so `Process.Start` is allowed.
- **Known trade-offs:** a child process spawned from the game can look unusual to AV/EDR; this design
  would be rejected by the official Dalamud plugin repo; there's per-lookup spawn overhead.
- **Verified:** in-process .NET `HttpClient` *can* reach GitHub static hosting
  (`raw.githubusercontent.com`, `*.github.io`) — only EC is blocked. This is what makes the planned
  replacement viable.

### Planned replacement (deferred, preferred)

A **GitHub Actions** cron job runs the `curl` crawler (curl works fine on the runners), builds a
compact static JSON index (`xivApiId → { loves, glamId }`), and publishes it to GitHub Pages / a
release asset. The plugin then just **downloads that static file in-process** and does in-memory
lookups — no subprocess, no in-game scraping, serverless. Not yet implemented.

## Key domain knowledge: the three item-ID spaces

The same item ("Ronkan Bandana of Scouting") has three unrelated IDs:

| ID | Example | Source | Used for |
|---|---|---|---|
| Game item ID (`XIVApiId`) | `25430` | SE `Item` sheet | what the **loot drop** reports; universal join key |
| EC ID | `14930` | EC's own DB | EC's `filter[<slot>Piece]=` query param |
| Lodestone hash | `1821cb137de` | SE Lodestone | not needed by us |

There is **no math relation** between them — only lookup tables. Conveniently, EC's
`/gear/<slot>/search` returns **all three together**, so it is its own game-ID ↔ EC-ID bridge.

## Architecture / file layout

```
src/GoodGlam/
  Plugin.cs                      entry point, wiring, /goodglam command
  Services.cs                    [PluginService] locator
  Configuration.cs               persisted settings (Enabled, LovesThreshold=100, CacheTtlHours=12)
  Glam/GlamSlot.cs               EquipSlotCategory -> EC slot; FilterParam = "<key>Piece"
  Glam/ItemResolver.cs           game item ID -> name + slot (Lumina Item sheet); HQ normalize; skips non-gear
  Glam/EorzeaCollectionClient.cs IGlamSource via curl.exe subprocess (search + popularity scrape)
  Glam/GlamPopularityService.cs  orchestration + per-item TTL cache + toast
  Loot/LootWatcher.cs            NeedGreed AddonLifecycle hook; reads CSLoot.Instance()->Items
  Windows/ConfigWindow.cs        ImGui settings UI
GoodGlam.json                    plugin manifest (DalamudApiLevel 15)
```

Key seam: **`IGlamSource`** abstracts the data source. The curl-backed `EorzeaCollectionClient` can
be swapped for a static-index client (the deferred GitHub Actions approach) without touching the
loot/resolver/notify code.

## MVP behaviour & decisions

- **"Popular"** = at least one glamour using the item has **≥ `LovesThreshold`** loves (default 100).
- **Detection point:** the `NeedGreed` roll window (`AddonLifecycle` PostSetup / PostRefresh), so you
  can be notified *before* you roll. Items are de-duplicated per window.
- **Notification:** native Dalamud toast (`INotificationManager`).
- **Caching:** per game-item-ID with a configurable TTL (default 12h) to stay polite to EC.

## Roadmap / next steps

1. **In-game smoke test** of the NeedGreed hook + toast (the only unverified path).
2. **Replace the curl subprocess** with the GitHub Actions crawler + static JSON index.
3. **MVP+1:** configurable filters mirroring EC (gender, date submitted, tags = style/theme/color,
   intended-for/job, level to equip). All map to EC query params.
4. **In-window annotation:** mark popular items directly on the Need/Greed window (better UX than a
   toast).
5. **MVP+2:** EC login → restrict notifications to glamours the user has saved.

## Notes for resuming

- `.dalamud/`, `bin/`, `obj/` are gitignored; restore `.dalamud/` per [Quick start](#quick-start-on-a-fresh-machine).
- Dalamud's distrib currently targets **net10.0** (revision 7312, assembly v15 → API level 15). If a
  future distrib bumps the major version, update `DalamudApiLevel` in `GoodGlam.json`.
- ImGui bindings are `Dalamud.Bindings.ImGui` (the newer binding package), not `ImGuiNET`.
