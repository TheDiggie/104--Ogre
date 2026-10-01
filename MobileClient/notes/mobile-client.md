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

## The phone has no space bar, so it has a Go button
Tags: design | ReqGo is the reference's Open key and has no AvatarAction, no target and no other way in - without a button of its own this client could walk around one room and never leave it

`SendReqGo(true)` is what space does in the game
(`OISKeyBinding.cpp:52` binds it, `ControllerInput.cpp:552` dispatches
it). The `true` sends the turn and the move out first, so the server
knows where you are standing and which way you face before it is asked
to take you through.

It is not the target row's Open and not `AvatarAction.Activate`: those
act on a thing you tapped, and ReqGo carries no argument at all. There
is no AvatarAction for it either, so it cannot be a hotbar button - it
has to be its own control. It sits in the bottom row between Log and
Settings, registered with `Panels.Opener` so it hides with the row.

Placing it by slot arithmetic alone drew it on top of Settings, which
is 96 wide where every other slot is 76: the shot read "SetGoings".
Look at the row, not at the numbers.

See also: godot-ui.md | the fixture -> fake-server.md

## RESTING, PARALYZED, SAVING across the middle
Tags: design | The game's UISplashNotifier, which the port did not have - three states stop the avatar dead and a phone had nothing on screen to say why

`SplashNotifier.cs` is `UISplashNotifier.cpp`: a list, the last one
added is the one shown, and the three strings are the game's own
(`Constants.h:934-936`). RESTING follows `Data.IsResting`, PARALYZED
`Data.Effects.Paralyze.IsActive`, SAVING `Data.IsWaiting`. The game's
fourth, PRESS A KEY, belongs to key learning and has no phone.

All three stop movement: `SendReqMoveMessage` returns early on any of
them and `TryMove` denies the step. A desktop player who cannot walk
still has a keyboard and a mouse to reason with; a thumb on a stick
that moves nothing is the same dead-button problem the lost-connection
overlay was built for.

Polled once a frame rather than subscribed - three bools, and the list
does the ordering the reference's handlers were there to preserve.

Played: Rest puts it up, Stand takes it down.

See also: godot-ui.md | the harness -> harness.md

## Autorun
Tags: design | The reference's AutoMove, a key there and a button here - a phone asks you to hold a thumb down for the whole walk, which a keyboard never did

`ControllerInput.cpp:566` flips `isAutoMove` on the key, and
`:607` cancels it on a manual forward or back. On a phone the stick IS
forward and back, so pushing it cancels; turning with the look drag
does not, which is the point - you run and steer.

`isAutoMoveOnMove` is mirrored too: switch autorun on while already
walking and the release that follows is the one you were already
making, so it spends that grace instead of cancelling the autorun you
just asked for.

The button is a toggle beside Go and follows the state rather than
owning it, because walking manually turns autorun off and the button
has to say so.

Played: Auto alone puts thirteen `ReqMove` on the wire with nothing
touching the screen, and a backward drag afterwards releases the
button in the next frame.

See also: godot-ui.md | the harness -> harness.md

## The actions window
Tags: design | UIActions.cpp - eleven actions, three of which had no way in on the phone at all

The hotbar is seeded with eight of the game's eleven avatar actions,
and nothing reached Dance, Point or GuildInvite - nor restored any of
the eight once a long press had cleared it. The target row is not the
same thing: those buttons act on what you tapped, and half of these
take no target.

`ActionsPanel` is the game's list, in that file's own order
(`UIActions.cpp:16-26`). A tap performs, as a double click does
there; the "+" puts it on the hotbar, which is what dragging does
there and what a phone cannot do while this panel covers the hotbar.
`ActionButtons.Bind` learned `AvatarAction`, with `SetToAction` for
the same reason Seed uses it.

Played: Dance puts an `Action` on the wire, and binding Point puts a
ninth button on the hotbar.

See also: the hotbar -> ActionButtons.cs | godot-ui.md

## Next target
Tags: design | The reference's NextTarget key - a phone needs it more than a mouse does

`ControllerInput.cpp:564` calls `Data->NextTarget()`, and the library
does all the choosing: objects within the target radii, attackable or
minimap-enemy, the ones not visited yet, nearest guild enemy before
nearest anything else (`DataController.NextTarget`). Nothing here
repeats any of that - the button is one call.

It earns its place on a phone. Tapping picks what is under your
thumb; a rat across a dark room is a few pixels wide, and a thumb is
not a mouse pointer.

Played: first press takes Boris the Outlaw, who is the minimap enemy,
second takes the duskrat - which is the library's order, guild enemies
before the rest.

See also: godot-ui.md

## Arrows and fireballs are drawn now
Tags: design, lessons | The library was already tracking and moving projectiles; nothing drew them, so combat happened with nothing in the air

`HandleShoot` resolves the source and the target, refuses a projectile
missing either, and `DataController.Tick` walks it toward the target
every frame (`:1078`). All that was missing was the sprite.

The frame is the library's own choice: `UpdateViewerAngle` says which
way the thing is presented from here, and the sprite cache is asked
for exactly that frame. Letting the renderer pick from an angle, as it
does for a creature, would subtract the viewer's angle a second time.

Two things had to be fixed before it could be seen at all:

- `Projectile`'s constructor takes a Speed and never assigns it, so
  every projectile built that way came out at speed zero - which is
  `MovementSpeed.Teleport`, and a teleporting projectile is on its
  target in one tick. One frame of arrow is no arrow. Fixed in the
  library.
- The harness had been reporting success while the build failed:
  `dotnet build | tail -1` prints "Time Elapsed" whether or not three
  errors came out above it. Two hours of "my code is not running"
  because it was not compiled. Grep the output for `error CS`.

Photographed at last: a silver arrow a few frames from the camera,
with the fixture firing every twelve messages under M59_SHOOT=1.

See also: WorldSync.cs | the fixture -> fake-server.md
