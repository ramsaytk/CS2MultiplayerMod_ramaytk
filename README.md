# CS2 Multiplayer Mod

## Introduction

CS2 Multiplayer Mod brings cooperative multiplayer to Cities: Skylines II. One player hosts a city, friends join, and everyone builds the same city together — roads, zoning, services, finances, and progression stay in sync as you play.

The mod is **experimental**. Back up your saves before hosting or joining, and expect bugs while development continues. The host is authoritative, and clients download the host's world when they join.

Feel free to join the development Discord server [here](https://discord.gg/NHKShnB5G9).

This mod and its source code are licensed under the [CS2 Multiplayer Mod Non-Commercial License](LICENSE). The license allows personal use, modification, and contributions to this project, but it does not allow commercial use, paid redistribution, or publishing clones/rebranded forks as someone else's project.

This project is not affiliated with, endorsed by, or sponsored by Colossal Order or Paradox Interactive.

## Requirements

- Cities: Skylines II with the official modding/UI support enabled.
- **All players must run the same version of the mod.** A mismatched build is refused at join, so update together.
- Players should also have **matching gameplay DLC**. Radio and content-only DLC do not affect a session.

## Install

The easiest way is through **Paradox Mods**: find the mod, add it to your playset, enable it, and restart the game if Cities: Skylines II asks you to.

## Hosting a game

1. Create a new city or open an existing one. If you use an existing save, **make a backup first**.
2. Open the in-game Multiplayer panel (or the mod settings).
3. Set your player name.
4. In the Host tab, choose the port, password, max players, LAN-only mode, and world re-sync interval.
5. Click **Host Session**.
6. For internet play, forward the chosen TCP port on your router, allow it through your firewall, and share the host address, port, and password **only with people you trust**.

## Joining a game

1. Click **Join Game** from the main menu, or open the Join tab in the mod settings.
2. Enter the host address, port, your player name, and the password.
3. Click **Join Session**.
4. Wait while the host's city downloads and loads — larger cities take longer. The dialog closes itself once you're in.

## What syncs

- The full host world on join, with periodic and on-demand re-sync (`/sync`) as a safety net.
- **Finances:** money, taxes, service fees, service budgets, loans, and policies.
- **Progression:** population, XP, milestones, and development-tree points and unlocks.
- **The world:** roads, buildings, zoning, bulldozing, terraforming, building and road upgrades, relocation, districts, surfaces, transit lines, and map-tile purchases.
- **Time & weather:** simulation speed, pause, weather, season, and the game clock.
- **Players:** chat, and live markers showing where each partner's camera is — including how high they're "flying".

Individual moving agents such as cars, pedestrians, pets, and service vehicles are **not** synced — each player simulates those locally for performance, and the full-world re-sync corrects any drift.

## Troubleshooting

- **City looks out of sync?** Run `/sync` in chat, or click **Sync World Now** in the mod settings. Clients pull a fresh save from the host; the host refreshes every connected player.
- **Can't join (protocol mismatch)?** You and the host are on different mod versions — update to the same build.

## Contributing

Contributions are welcome as long as they follow this repository's license. Keep attribution intact, submit changes through this project, and do not publish paid, monetized, or rebranded copies of the mod.
