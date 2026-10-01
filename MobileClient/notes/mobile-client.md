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

## The library is not silent about a missing target
Tags: lessons | A targeted spell with no target DOES print "This spell requires a target." - grepping three other wordings and finding nothing is not evidence of absence

`SendReqCastMessage` builds its targets from the highlighted object, or
yourself, or your target, and if the spell needs one and none is there it
adds "This spell requires a target." to `Data.ChatMessages` and returns
without sending (`BaseClient.cs:1805`, rate-limited by `CanReqCast` so it
cannot spam). Skills do the same at `:1939`.

I added a second line of my own on the stated grounds that nothing in the
library said anything, and shipped a duplicate. Two mistakes made it:
grepping for "no target", "needs a target" and "target required" and
reading the empty result as an absence, and cropping the last few chat
lines instead of looking at the whole frame, which put the library's own
red line one row below the crop. Reverted in 4bf67fa.

`SendReqAttackMessage(RoomObject)` is the genuinely silent one - no else
branch at all - and it hard-gates on line of sight.

See also: the hotbar -> HotbarStore.cs | lessons -> rulings.md
