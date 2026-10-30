# Increased Enemy Count (Outward Definitive Edition)

BepInEx 5 plugin that multiplies the number of enemies in every encounter (x2 by default). Bosses can optionally be included.

## How it works
Outward places every enemy in the area scene ahead of time. That includes the roaming "squads", which wait in a reserve and are switched on later. When an area finishes loading, the mod copies each eligible enemy before any of its scripts start, then gives the copy:

- a **predictable character UID** (`<original UID>_x1`). The area save keys characters by UID, so dead copies stay dead until the area resets, the same as the original.
- **new Photon view IDs** in a block of its own, around 2,000,000,000. Photon reads an ID as "player number × 1000 + slot", and that block belongs to player numbers that can never exist, so it can't collide with the game, other mods (SideLoader, Raid Mode) or real players. The copy's owner is set to 0, like the area's own enemies, so the host controls it. Each area has its own run of IDs (up to 999,000), so there's no practical limit. If two objects share an ID, Photon destroys the older one, which would be the original.
- a place in the original's **AI squad**, if it has one, so it spawns, wanders and fights with that group.
- a position a couple of metres away, snapped to the ground and navmesh.

Copies don't get the original's quest-on-death trigger, so quests only advance once. Enemies that start switched off and are switched on later by a script (ambushes, events) bring their copies with them.

The first time you enter an area that's already cleared, copies of dead enemies are loaded as dead too.

## What counts as an enemy
AI characters, except those in the Player (friendly NPC), Merchants or NONE factions, and anything with dialogue, a shop or a trainer.

## What counts as a boss (only matters when `IncludeBosses = false`)
Any one of these:
- its death advances a quest (`QuestTargetsAreBosses`)
- base max health >= `BossHealthThreshold` (default 1000)
- its name contains a word in `BossNameKeywords` (default `Elite,Lich,Grandmother,Calixa`)

Outward has no built-in "boss" flag, so this is a heuristic. With `LogEachEnemy = true`, every enemy's name, HP and boss decision is written to `BepInEx/LogOutput.log`. Use that to tune the settings, and use `ExcludedNames` for anything that should never be copied.

## Online co-op (including Raid Mode's larger parties)
Every player's game makes the copies itself, in exactly the same way: same enemies, same order, same UIDs, same network IDs. Because of that, the game's normal syncing treats the copies like any other enemy. The host runs their AI, and everyone else sees their movement, health and deaths, including players who join mid-session. Party size doesn't matter.

- **Everyone needs the mod.** Each player's game announces that it has it. If someone doesn't, the host gets an on-screen notice, and with `RequireModOnAllPlayers` on (the default) no extra enemies are added to areas loaded while that player is in the session. Without the mod, a player can't see the copies but can still be hit by them.
- **Only the host's settings count.** The host shares its settings with the session, and everyone else uses them instead of their own. A player joining mid-area uses the settings the host loaded that area with.
- **Mismatch check.** The host shares which enemies it copied in each area. Each other player compares that with what their game made: extra copies are removed, and missing ones trigger an on-screen notice. Likely causes are a different game language (only matters for the name-based boss/exclusion lists) or mismatched mods.
- Changing settings mid-session applies from the next area load for everyone.

## Config
`BepInEx/config/catalysten.increasedenemycount.cfg` in the r2modman profile, created on first launch. Changes apply on the next area load.

| Setting | Default | |
|---|---|---|
| General.Enabled | true | master switch |
| General.EnemyMultiplier | 2 | total count of each enemy (1–10) |
| General.SpawnSpacing | 2.0 | metres between original and copy |
| General.ExcludedNames | "" | comma list, never copied |
| Bosses.IncludeBosses | false | multiply bosses too |
| Bosses.BossHealthThreshold | 1000 | 0 disables the HP check |
| Bosses.BossNameKeywords | Elite,Lich,… | comma list |
| Bosses.QuestTargetsAreBosses | true | |
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

## Known limits
- Arena wave spawners and enemies spawned by skills (summons) aren't affected.
- Uninstalling leaves a few orphaned copy entries in area saves. The game ignores them.
