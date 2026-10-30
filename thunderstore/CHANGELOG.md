# Changelog

## 1.2.3
- Fixed: NPCs that are peaceful for story reasons no longer get hostile duplicates. Copies now follow what the game does to the original: if a script makes it peaceful (e.g. the slavers in Vendavel Fortress before you escape) or switches it off (e.g. the hostile Immaculate that is swapped for the friendly one), its copies are hidden, and they come back if it turns hostile or is switched on again.
- Co-op: all players need 1.2.3.

## 1.2.2
- Updated README (full settings table) and package description.

## 1.2.1
- Updated README.

## 1.2.0
- First Public Release.
- New ReadMe.

## 1.1.0
- Enemies are now Standard, Unique or Boss, using the lists on the official Outward wiki, and each type has its own multiplier slider (all 2x by default).
- New "Always standard" keyword box: matching enemies use the standard multiplier, even if they're a boss or unique.
- "Never copy" keywords still win over everything.
- Removed the old boss guessing (health threshold, name keywords, quest targets) and the single multiplier. Re-check your settings after updating.
- Co-op: all players need 1.1.0, because the way copies are set up changed.

## 1.0.0
- Multiplies every placed enemy in each area (2x by default, up to 10x), including roaming groups and event-activated ambushes.
- Copies keep their full AI, join their original's group, and are saved and restored with the area.
- Optional boss multiplication, with configurable detection (quest targets, health threshold, name keywords).
- Online co-op support for any party size, including Raid Mode: the host's settings are shared, every player's game makes matching copies, and mismatches or players without the mod are reported on screen.
- Music keeps playing in crowded fights, and the number of sounds that can play at once is raised (configurable).
- Settings editable in-game with Outward Config Manager.
