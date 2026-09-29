# The mobile client

`MobileClient/` - Godot 4.7, C#, eleven panels, built to reference parity
with `Meridian59.Ogre.Client` and then played until it was usable.

## The panels
Tags: architecture | Each panel owns its opener button and registers it with Panels.Opener so the view can hide the row as a row

Chat, spells/skills, inventory, loot, buy, trade, look, amount prompt,
hotbar, minimap, lost-connection. `GameView.cs` gates all of it on
`inWorld = _wasInGame || _client.Data?.AvatarObject != null` - gating on
`EnteredGame` instead left a newly created character in a room with no
interface at all, because character creation skips `UseCharacter`.

See also: godot-ui.md | Panels.cs

## The hotbar persists per character
Tags: architecture | HotbarStore writes Num, ButtonType, NumOfSameName and Name only - Data is re-resolved by name by DataController, and Label is a key binding a phone does not have

An item is matched by name AND NumOfSameName, because names in this game
are not unique. A stored object id would be worse than useless: ids are
per session.

See also: HotbarStore.cs

## Known gaps, deliberately left
Tags: design | Minimap dots pop out whole at the rim instead of clipping in half (MiniMapCEGUI.h:160-175) and the radius is ~2% off; hotbar alias buttons do not exist because no alias list exists anywhere in the port

See also: ../../CLAUDE.md

## Targeting outlines the target in red
Tags: design | Ashton's standing requirement: clicking a thing to attack it outlines it red, as the game does

See also: godot-ui.md
