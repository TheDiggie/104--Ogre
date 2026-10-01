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

## First run, and the half-installed game
Tags: gotchas, lessons | The "unpacked" test was "the folder holds one .roo", which is true seconds into a ~470MB copy - so an interrupted first run left the game permanently half-installed with no message anywhere

`M59Paths.HasContent` is the right question to ask of a folder the
PLAYER pointed us at and was entirely the wrong one to ask of our own
copy. The destination is created first and the files are written in pack
order, so "it exists and holds a .roo" turned true within seconds of a
copy that takes minutes. Background the app, run the battery down or
fill the disk in that window and the next launch believed it was done:
a working login screen, then a blank world behind a live HUD, because
`RenderFrame` draws nothing without a room. No message, and clearing app
data the only cure.

What it is now:

- A completion marker, `user://resource/.unpacked`, written after the
  last byte of the last file and nowhere else. Its absence means
  unfinished, which is a thing we can resume - files already on disk at
  the right length are skipped, so an interrupt near the end costs
  seconds rather than the whole copy again.
- The marker carries a stamp: `application/config/version` plus the
  pack's file count and total bytes. The old marker was content, so a
  new APK's resources were ignored for ever - `version/code` was 1 for
  every build and is not readable at runtime anyway. Bump the version
  and devices replace their data; forget to, and changed content still
  replaces itself. A stamp that differs also prunes files the new build
  does not ship.
- `DirAccess.GetSpaceLeft()` before the first byte, with the client's
  own refusal screen and a Try again button. The old per-file catch
  logged `e.Message` to logcat and carried on, so a full disk produced
  one failure per file for hundreds of files, left the part-written ones
  on disk, and still returned a count above zero - which the caller
  threw away. A write that fails now stops the run and says so.
- `UnpackIfNeeded` returns an `UnpackReport`, and `GameView` only
  reaches `FindResources` on `Ok`.

Measured, not reasoned: a 6MB filesystem refuses before writing anything
and leaves `NeedsUnpack` true; deleting the marker, truncating one file
and deleting another makes the next run write exactly those two and skip
the other seven.

See also: M59Paths.cs | the screen -> UnpackScreen.cs

## Sound shipped, and did not arrive
Tags: gotchas, lessons | Godot IMPORTS .ogg, so the pack holds `X.ogg.import` and a compiled `.oggvorbisstr` and NOT `X.ogg` - and the unpack skipped `*.import`, so the phone had no sound and no music at all

Established by exporting a pack and reading its file list, which is the
only way this was ever going to be settled:

```
Storing File: res://.godot/imported/AMBCave.ogg-<md5>.oggvorbisstr
Storing File: res://resource/AMBCave.ogg.import
```

and `DirAccess.GetFiles("res://resource")` in the exported build returns
`AMBCave.ogg.import` with no `AMBCave.ogg` beside it. The library reads
sound and music as `.ogg` off the disk (`ResourceManager.cs:583,595`),
so every one of them was missing on the device, silently.

The cure is the one `MobileClient/sky/` has carried since the skybox
went in: an `importer="keep"` sidecar makes Godot pass the file through,
and the same export then stores `res://resource/AMBCave.ogg` itself and
no sidecar. `stage-resource.sh` writes them. A `.gdignore` looks like a
shortcut and is not - it takes the whole folder out of the export,
`include_filter` and all; the pack came out with no resource folder in
it.

The walk also recurses now. It was one `GetFiles()` on the top level, so
nothing in a subfolder ever reached the device, and `include_filter`
does ship subfolders - `resource/rooms/GreenPlantation.roo` is in the
pack. A surviving `.import` with no source beside it is now counted and
said out loud on screen, because "the game has no sound" is otherwise a
bug report with nothing in it.

See also: M59Paths.cs | the staging -> ../stage-resource.sh

## A riddle after login, sixty times a second
Tags: gotchas, lessons | `Update` runs every message handler there is, so a corrupt .roo surfaces in Pump's catch - which gave one small stackless line, a frozen world, and an unbounded log

`Fail($"Update: {type}: {message}")` and `return`. The player got a
label with no stack and no filename, the world stayed on its last frame
with every button still looking live, and because `Fail` appended to
`_log` without trimming while the other two appenders trimmed to six,
a persistent exception grew that list by about sixty strings a second
for as long as the app was left up - and `RenderFrame` joined all of
them into the status label every one of those frames. A leak and a
slowdown out of an error path.

`PumpFailed` now: the whole trace to the console every time, the room
file on screen (the nearest thing to a filename the library gives us
here, and a corrupt room is the likeliest cause), and the same fault
three frames running escalates to the full-screen scrollable report and
stops pumping. One throw may be one bad object in one message and
killing the session over it would be a regression - the old `return`
was right about that much. The same throw every frame is not transient.

Every append to `_log` goes through `Note` now, which is the only place
that trims. `mobile-client.md` carried the lesson this path did not
follow: a message without a stack is a riddle (`M59Assets.cs:66`).

See also: GameView.cs | lessons -> rulings.md

## The look flags are the library's, and a view must write them back
Tags: gotchas, lessons | Data.LookObject/LookSpell/LookSkill/LookPlayer `.IsVisible` are the source of truth for the look window; Close that does not clear its flag is re-entered next frame and the session is bricked

`DataController.cs:2900-2928` treats the object, spell and skill flags as
mutually exclusive (each handler raises its own and lowers the other
two). `LookPlayer` is the odd one: raised at `:2776` and lowered by
nobody, so a player look and another look can be up together. `LookPanel`
therefore keeps a table of kinds, each with how to read its flag, how to
CLEAR it and how to draw (`LookPanel.cs:485-505`), shows the last to
arrive, and Close is `_showing?.Hide(_data)` (`:361`). A kind cannot be
built without a `Hide` (`:457-466`), which is the point: Close once wrote
back only two of four flags, the spell and skill looks came straight
back, and the shade over the screen ate every touch. There is no back
or Escape path in the client to get out of it (the wire showed zero
`ReqMove` from a 150-frame drag).

Any new view that shows one of these must write its flag back on close.

See also: LookPanel.cs | the harness trap -> harness.md

## Never write the avatar's HorizontalSpeed
Tags: gotchas, lessons | Zero is `MovementSpeed.Teleport` to the library, so zeroing it on stick release turned every mid-air fall into one tick and killed step-up easing

`MovementSpeed.cs:26,38` make `SPEED_NONE`, `Teleport` and 0 the same
value, and `RoomObject.UpdatePosition` sets `hDiff` to 0 - no gravity, no
step-up - when `horizontalSpeed == Teleport` (`RoomObject.cs:1102`).
`Settle` used to write 0 on release, which on a phone is constantly and
exactly when you go over an edge: off barlmarket's ledge 41 ticks became
one. Nothing in the reference writes the field (only `StartMoveTo` does,
25 or 55); the only legitimate writer here is the library's own
`RoomObject.cs:1265`. `Settle` now only forces a position send, which
`SendReqMoveMessage` already throttles (`BaseClient.cs:1620-1626`).
`grep "HorizontalSpeed *="` in `MobileClient/` should stay empty.

See also: GameView.cs | the same trap, for projectiles -> "Arrows and fireballs"

## Two library warts in the wire classes
Tags: gotchas | A .NET sender of a skill look sends the spell look's message type, and the byte[] ChangeMessage constructor ignores its start index

- `LookSkillMessage`'s constructor passes `MessageTypeGameMode.LookSpell`
  to its base (`LookSkillMessage.cs:63-64`), so any .NET SENDER of a skill
  look emits the wrong PI. Not fixed.
- `ChangeMessage(byte[] Buffer, int StartIndex = 0)` passes
  `StartIndex = 0` to its base (`ChangeMessage.cs:81-82`) - an assignment,
  so a nonzero start index is silently dropped. Not fixed.

See also: wire-format.md

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

## No vertical tiling
Tags: design, gotchas | WF_NO_VTILE was honoured only on walls drawn see-through, so 14% of the game's sidedefs tiled a texture up the wall that the game draws once

58,043 of the 362 rooms' 406,792 sidedefs carry the flag. This
renderer applied it only where the wall was actually drawn
see-through, and only on the negative side of the UV - so a solid
one, or a top-down one, repeated. In Raza's forest, where every tree
and every building face carries it, that is most of what you look at.

The rule is now: a no-vtile middle draws the one tile its mapping
lands on, and nothing outside it. A see-through wall then shows
whatever is behind, as it always did; a solid one shows the void,
because the library's answer is to shorten the quad
(`RooWall.cs:1304`) and there is nothing behind a one-sided wall to
see. Middle parts only - the library's own comment says the bottom
"creates strange holes".

Two things this needed, both kept:

- `--at X,Y` and `--face DEG` on the offline view, because the camera
  went to the middle of the roomiest leaf and the first hour went
  into photographing rooms with no such wall in shot.
- The container's resource folder had 167 of the game's 1,581 room
  textures, so Raza rendered as flat grey - the `t == null` colour -
  and the probe that should have lit up never ran. The 71 that room
  needs are staged now.

See also: Renderer.cs | godot-ui.md
