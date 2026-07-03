<p align="center">
  <img src="src/GoodGlam/Assets/Logo.svg" alt="GoodGlam logo" width="120" height="120" />
</p>

# GoodGlam

[![CI](https://github.com/forteddyt/GoodGlam/actions/workflows/ci.yml/badge.svg)](https://github.com/forteddyt/GoodGlam/actions/workflows/ci.yml)
[![Coverage](https://forteddyt.github.io/GoodGlam/badge_linecoverage.svg)](https://forteddyt.github.io/GoodGlam/)

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **FINAL FANTASY XIV** that watches the **Need/Greed/Pass roll window** and notifies you when a rollable item is used in a *popular* glamour on [Eorzea Collection](https://ffxiv.eorzeacollection.com/glamours) - so you can decide whether an unassuming dungeon/trial/raid drop is actually worth rolling on.

> ⚠️ Third-party tools violate Square Enix's Terms of Service. Use at your own risk.

> 📖 **Documentation:** installation, usage, configuration, and developer guides live in the **[project wiki](https://github.com/forteddyt/GoodGlam/wiki)**.

## How it works

1. GoodGlam watches the **Need/Greed/Pass roll window** and reads each rollable item as it appears.
2. For each item it checks **Eorzea Collection** for the most-loved glamour that uses it.
3. If that glamour meets your **threshold**, you **are notified** and a link to the glam is shared.

So an unassuming dungeon/trial/raid drop never slips by unnoticed when it is actually glam-worthy. Results are cached to stay polite to Eorzea Collection, and the threshold, filters, and notifications are all configurable.

## Install

GoodGlam is distributed through a **Dalamud custom plugin repository**. In-game, open `/xlsettings` -> **Experimental** -> **Custom Plugin Repositories**, add the URL below, then install **GoodGlam** from `/xlplugins`:

```
https://forteddyt.github.io/GoodGlam/repo.json
```

Enable **Get plugin testing builds** in the same Experimental settings to opt into the rolling dev channel. Full steps (and building from source instead) are in the **[Installation guide](https://github.com/forteddyt/GoodGlam/wiki/Installation)**.

See the **[project wiki](https://github.com/forteddyt/GoodGlam/wiki)** for installation, usage, configuration, and developer documentation.

> ℹ️ This plugin was developed with AI assistance.
