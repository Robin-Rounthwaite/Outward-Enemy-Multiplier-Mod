# Increased Enemy Count

More enemies in every encounter. Every bandit camp, hyena pack, dungeon, unique enemy and boss gets 2x the enemies by default, up to 10x. Standard enemies, unique enemies and bosses each have their own slider.

## Features

- **Every encounter, everywhere.** Copies are made of the enemies the game places in each area, including roaming groups that appear later and ambushes that are switched on by events.
- **Copies behave like the originals.** They wander, notice you, fight and flee like the enemy they copy, and join its group.
- **Copies are saved like the originals.** Killed copies stay dead until the area resets, just like the originals.
- **Quests aren't affected.** Killing a copy doesn't count twice towards a quest.
- **Three enemy types, three sliders.** Standard enemies, unique enemies and bosses each get their own multiplier. Bosses and uniques are the ones listed on the [official Outward wiki](https://outward.wiki.gg/wiki/Enemies). You can override the lists by keyword.
- **Co-op works, including larger [Raid Mode](https://thunderstore.io/c/outward/p/SpicerXD/Raid_Mode/) parties.** Everyone sees the same extra enemies, using the host's settings.
- **Audio fixes for crowds.** Music no longer cuts out with hundreds of enemies around, and more sounds can play at once, so big groups don't sound choppy.

## Settings

Settings are in `BepInEx/config/catalysten.increasedenemycount.cfg`. You can also edit them in-game with [Outward Config Manager](https://thunderstore.io/c/outward/p/Mefino/Outward_Config_Manager/) (press F5). Most changes apply the next time an area loads.

| Setting | Default | What it does |
|---|---|---|
| Standard enemies | 2 | Total of each standard enemy: 2 = double, up to 10 |
| Unique enemies | 2 | Total of each unique enemy (e.g. Ghost of Vanasse), up to 10 |
| Bosses | 2 | Total of each boss (e.g. Elite enemies), up to 10 |
| Spawn spacing | 2 m | How far copies stand from the original |
| Never copy (keywords) | — | Comma-separated keywords; matching enemies are never copied (e.g. `Pearlbird`) |
| Always standard (keywords) | — | Comma-separated keywords; matching enemies use the standard slider, even if they're a boss or unique |
| Require mod on all players | on | Co-op host: add no extra enemies while someone lacks the mod |
| Keep music playing | on | Stops music cutting out in crowds |
| Max audible sounds | 128 | Sounds that can play at once (game default 32); needs a restart |

Bosses and unique enemies are matched by their English names, so in other game languages every enemy uses the standard slider. "Never copy" wins over "Always standard". The BepInEx log lists every enemy with its type and how many copies it got.

## Co-op

- **Everyone in the session needs the mod**, at the same version. Players without it can't see the extra enemies, but can still be hit by them. By default the host's game adds no extra enemies while someone is missing the mod, and the host gets an on-screen notice.
- **Only the host's settings are used.** Other players' own settings don't matter in co-op.
- **If anything doesn't match** between a player and the host (for example because of different mods), that player gets an on-screen notice. Leaving and re-entering the area usually fixes it.

Co-op support is new. If you run into problems, please report them with your `BepInEx/LogOutput.log` attached.

## Known limits

- Arena wave spawns and summoned creatures aren't multiplied.
- High multipliers in crowded areas mean a lot of enemies: Chersonese at 10x has 760 (76 originals plus 684 copies).
- Removing the mod leaves a few unused entries in area saves. The game ignores them.

## Installation

Use a mod manager such as r2modman or Thunderstore Mod Manager. To install by hand, put `IncreasedEnemyCount.dll` in `BepInEx/plugins/`. Requires the BepInEx pack for Outward. Built for Outward: Definitive Edition.
