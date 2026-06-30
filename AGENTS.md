# AGENTS.md

Guidance for coding agents working in this repository. Keep this file lean: it covers the high-level shape, conventions, and commands. **For anything deeper, search the wiki** - it is the source of truth for user and developer documentation.

## What this is

GoodGlam is a **.NET 10 / C# Dalamud plugin** for FINAL FANTASY XIV. It watches the Need/Greed roll window and flags drops that are used in *popular* glamours on Eorzea Collection. It builds and runs on **Windows and Linux**.

## Build & test

The Dalamud dev libraries must be available, either restored into `.dalamud/` (gitignored) or via the `DALAMUD_HOME` environment variable. On Linux, `DALAMUD_HOME` is easiest:

```bash
export DALAMUD_HOME="$HOME/.xlcore/dalamud/Hooks/dev"   # or restore .dalamud/ (see wiki: Development)

dotnet restore GoodGlam.slnx
dotnet build   GoodGlam.slnx -c Release --no-restore
dotnet test    GoodGlam.slnx -c Release --no-build --settings coverlet.runsettings
```

The plugin alone builds via `dotnet build src/GoodGlam/GoodGlam.csproj -c Release`.

## Architecture (high level)

Loot detection -> item resolution -> Eorzea Collection lookup -> popularity verdict -> history + logo glow. Two key seams keep the layers decoupled:

- **`IGlamSource`** - where popularity data comes from (today the live Eorzea Collection client).
- **`IEcTransport`** - how EC is reached (managed `HttpClient` first, `curl.exe` fallback).

Honor these interfaces when adding data sources or transports. The component overview and data flow are in the wiki (**Architecture**, **Data transport**).

## Conventions

- Nullable reference types **enabled**; implicit usings **enabled**; C# `latest`.
- Use the **`Dalamud.Bindings.ImGui`** bindings, **not** `ImGuiNET`.
- Reach Dalamud services through the `Services` locator.
- There is no `.editorconfig` - match the formatting and naming of nearby files.

## Testing

- xUnit + FluentAssertions + FakeItEasy in `tests/GoodGlam.Tests` (mirrors `src/`); internals are exposed via `InternalsVisibleTo`.
- `tests/GoodGlam.IntegrationTests` holds **live** end-to-end tests that drive the real Eorzea Collection client (the `/goodglam check` pipeline below the Lumina resolve step). They are blocking and require EC reachability; both suites feed the merged coverage report. See the wiki **Development** page.
- Add/update tests for behavior you change. The `NeedGreed` in-game hook path and Lumina item resolution can't run in CI; verify those manually (e.g. `/goodglam check <itemId>` in-game) and note it in the PR.

## Gotchas

- **Transport:** on native Windows EC blocks .NET's HTTP via Cloudflare TLS fingerprinting, so the plugin shells out to `curl.exe`; under Wine/Linux the in-process client works and `curl.exe` doesn't. The fallback is automatic - don't add OS sniffing. See the wiki **Data transport** page.
- **No product-direction in the repo.** Roadmap/status decisions live in GitHub Issues/Projects, not in committed docs.
- **The wiki is a submodule** (`forteddyt/GoodGlam.wiki`). If it isn't checked out, run `git submodule update --init wiki`. Edit pages under `wiki/`, commit there, push, then bump the submodule pointer.

## Where to look next (wiki pages)

- **Development** - build/test details, CI & coverage, high-level layout.
- **Architecture** - components, the key seams, the three item-ID spaces.
- **Data transport** - the Cloudflare/`curl.exe` design.
- **Contributing** - conventions and PR flow.
- **Usage** / **Configuration** / **Installation** / **Troubleshooting** - user-facing behavior.
