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
Tags: design | Hotbar alias buttons do not exist, because no alias list exists anywhere in the port

The minimap's radius is ~2% tighter than the reference's, and that one
stays: the game's map fills its square window and its walls run under
the frame, while ours is cut inside the dial texture's rim, which has a
rim to run under. MiniMap.cs says so where the number is. The dots
themselves are now cut to that rim, as the game cuts them.

See also: the client -> ../README.md

## Targeting outlines the target in red
Tags: design | Ashton's standing requirement: clicking a thing to attack it outlines it red, as the game does

See also: godot-ui.md

## A press the library drops silently now says so
Tags: design, lessons | A targeted spell with no target sends nothing and neither the library nor the Ogre client says a word; on a phone that is indistinguishable from a dead button

`SendReqCastMessage` builds its targets from the highlighted object, or
yourself, or your target, and if the spell needs one and none is there
it returns without sending (`BaseClient.cs:1717-1743`). A desktop
survives that - the target is highlighted under the mouse you are
already holding. A phone does not: a tap with no sound, no animation and
no text reads as broken, and the player taps again.

So the hotbar says it in the chat log. The check runs BEFORE the
dispatch and the dispatch still happens, so it can only ever add a line.
Deliberate divergence, same spirit as the lost-connection overlay.

See also: the hotbar -> HotbarStore.cs | godot-ui.md
