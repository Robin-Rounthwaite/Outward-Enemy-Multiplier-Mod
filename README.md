# Increased Enemy Count (Outward Definitive Edition)

BepInEx 5 plugin that multiplies the number of enemies in every encounter, with separate multipliers for standard enemies, unique enemies and bosses (all x2 by default).

## How it works
Outward places every enemy in the area scene ahead of time. That includes the roaming "squads", which wait in a reserve and are switched on later. When an area finishes loading, the mod copies each eligible enemy before any of its scripts start, then gives the copy:

- a **predictable character UID** (`<original UID>_x1`). The area save keys characters by UID, so dead copies stay dead until the area resets, the same as the original.
- **new Photon view IDs** in a block of its own (9 reserved per enemy, the most any slider allows, so IDs never depend on settings or on how an enemy was classified), around 2,000,000,000. Photon reads an ID as "player number × 1000 + slot", and that block belongs to player numbers that can never exist, so it can't collide with the game, other mods (SideLoader, Raid Mode) or real players. The copy's owner is set to 0, like the area's own enemies, so the host controls it. Each area has its own run of IDs (up to 999,000), so there's no practical limit. If two objects share an ID, Photon destroys the older one, which would be the original.
- a place in the original's **AI squad**, if it has one, so it spawns, wanders and fights with that group.
- a position a couple of metres away, snapped to the ground and navmesh.

Copies don't get the original's quest-on-death trigger, so quests only advance once. Enemies that start switched off and are switched on later by a script (ambushes, events) bring their copies with them.

The first time you enter an area that's already cleared, copies of dead enemies are loaded as dead too.

## What counts as an enemy
AI characters, except those in the Player (friendly NPC), Merchants or NONE factions, and anything with dialogue, a shop or a trainer.

## Standard, Unique and Boss enemies
Each enemy is one of three types, and each type has its own multiplier:
- **Boss** and **Unique**: the enemies on the official wiki's [Bosses](https://outward.wiki.gg/wiki/Enemies/Bosses) and [Unique](https://outward.wiki.gg/wiki/Enemies/Unique) lists, kept in `src/EnemyCategories.cs`. Names are matched exactly (ignoring case and apostrophe style), because some names overlap: "Ash Giant" is standard, "Ash Giant Highmonk" is a boss. The names are English, so in another game language everything counts as standard.
- **Standard**: everything else.

Two keyword boxes override the lists:
1. `ExcludedNames`: never copied. This wins over everything.
2. `IncludedNames`: treated as standard, whatever the lists say.

With `LogEachEnemy = true`, every enemy's name, HP, type and copy count is written to `BepInEx/LogOutput.log`. If an enemy is misclassified, add it to `EnemyCategories.cs`.

## Online co-op (including Raid Mode's larger parties)
Every player's game makes the copies itself, in exactly the same way: same enemies, same order, same UIDs, same network IDs. Because of that, the game's normal syncing treats the copies like any other enemy. The host runs their AI, and everyone else sees their movement, health and deaths, including players who join mid-session. Party size doesn't matter.

- **Everyone needs the mod.** Each player's game announces that it has it. If someone doesn't, the host gets an on-screen notice, and with `RequireModOnAllPlayers` on (the default) no extra enemies are added to areas loaded while that player is in the session. Without the mod, a player can't see the copies but can still be hit by them.
- **Only the host's settings count.** The host shares its settings with the session, and everyone else uses them instead of their own. A player joining mid-area uses the settings the host loaded that area with.
- **Mismatch check.** The host shares which enemies it copied in each area. Each other player compares that with what their game made: extra copies are removed, and missing ones trigger an on-screen notice. Likely causes are a different game language (the wiki lists and keyword boxes match names) or mismatched mods.
- Changing settings mid-session applies from the next area load for everyone.

## Config
`BepInEx/config/catalysten.increasedenemycount.cfg` in the r2modman profile, created on first launch. Changes apply on the next area load.

| Setting | Default | |
|---|---|---|
| General.Enabled | true | master switch |
| General.SpawnSpacing | 2.0 | metres between original and copy |
| General.ExcludedNames | "" | keywords, never copied (wins over everything) |
| General.IncludedNames | "" | keywords, always treated as standard |
| Multipliers.StandardEnemies | 2 | total count of each standard enemy (1–10) |
| Multipliers.UniqueEnemies | 2 | total count of each unique enemy (1–10) |
| Multipliers.Bosses | 2 | total count of each boss (1–10) |
| Multiplayer.RequireModOnAllPlayers | true | host only: skip copies while anyone lacks the mod |
| Audio.PrioritizeMusic | true | stop music cutting out when many enemies make noise |
| Audio.MaxAudibleSounds | 128 | sounds audible at once (Unity default 32); restart to apply |
| Debug.LogEachEnemy | true | per-enemy log lines |

## Building
Needs the .NET SDK (any version that can build `net472` SDK-style projects, e.g. .NET 6/8).

```
dotnet build -c Release
```

The project compiles against the game's own Mono libraries and the profile's BepInEx/Harmony, so no targeting pack is needed. After a build, the DLL and PDB are copied to
`%APPDATA%\r2modmanPlus-local\OutwardDe\profiles\modding-experiments\BepInEx\plugins\catalysten-IncreasedEnemyCount\`.
If the game or profile moves, edit `GameManaged` / `ProfileDir` in the `.csproj`.

## Releasing on Thunderstore
The package files live in `thunderstore/`: `manifest.json`, the player-facing `README.md`, `CHANGELOG.md` and `icon.png` (must be 256×256).

1. Set the same version in `thunderstore/manifest.json` (`version_number`) and `src/Plugin.cs` (`VERSION`), plus `src/AssemblyInfo.cs`.
2. Add a `CHANGELOG.md` entry.
3. Run `powershell -ExecutionPolicy Bypass -File thunderstore\pack.ps1`. It builds the mod, checks the versions match and the icon is 256×256, and writes `thunderstore/dist/IncreasedEnemyCount-<version>.zip`.
4. Upload that zip on Thunderstore (Outward community → Upload).

If a change would make copies come out differently between versions (IDs, UIDs, which enemies are copied, or their order), also bump `PROTOCOL` in `src/NetSync.cs`, so co-op players on mismatched versions are told.

## Known limits
- Arena wave spawners and enemies spawned by skills (summons) aren't affected.
- Uninstalling leaves a few orphaned copy entries in area saves. The game ignores them.
