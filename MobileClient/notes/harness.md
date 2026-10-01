# The play harness (SceneShot)

`MobileClient/SceneShot.cs` runs the client headless and drives it, which
is how every real bug in this port was found. Diffing against the
reference found the cosmetic ones; playing found the ones that made the
client unusable.

## Running it
Tags: process | xvfb-run plus M59USER/M59PASS; --press is a comma list of steps, --shots writes out-1.png per step, --char skips the picker

Godot lives at
`/tmp/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.
Steps in `--press`: a bare node name, `@tap:XxY`, `@hold:<Node>`,
`@type`, `@submit`, `@name:<Node>`, `@slot`, `@drag:<from>><to>@<frames>`,
`@obj:<name>`.

Fixture switches on the fake server: `M59_STATCHANGE=1`, `M59_NEWS=1`,
`M59_CHATFLOOD=1`, `M59_PARALYZE=1`, `M59_WAIT=1`.

See also: the fixture -> fake-server.md

## The invariant tour
Tags: process, lessons | Build a sequence where step N leaves the world exactly as step 1 did, shoot every step, stack the same crop from each frame, and look - three bugs came out of it

Anything that differs between the first frame and the last is a bug you
would otherwise have to notice by luck.

See also: the client -> ../README.md

## Two harness bugs that hid real ones
Tags: lessons, gotchas | Read the box text BEFORE emitting the submit signal, and match buttons on IsVisibleInTree() not Visible

The submit step printed the text after emitting, and the handler clears
the box - so every successful send reported `submitted ""`, which hid the
fact that plain text sent nothing at all. Separately, matching on a
node's own `Visible` flag pressed buttons inside hidden parents. Both are
fixed; both cost days.

See also: SceneShot.cs

## What has been played end to end
Tags: process | Kept so the next session tests something new rather than re-proving these; each was watched on the wire, not just on screen

- Every bottom-row panel opens and closes; Map is a toggle with no Close,
  which is right.
- Chat: plain text sends `SayTo` with ChatTransmissionType.Normal and the
  room answers.
- Targeting: tap outlines in red, tapping elsewhere clears it, and the
  library resets TargetID itself when the target leaves the room.
- Attacking: three hits, `Remove`, the object goes and the outline with it.
- Buying: `ReqBuyItems` then `InventoryAdd`; a stackable carries its
  count, a single item sends x0. Two stacks of one item do NOT merge, and
  that is the game - hence `NumOfSameName`.
- Looting: `ReqGet`, the item lands in the pack and leaves the floor, and
  pressing Loot again opens nothing because there is nothing to loot.
- Trading: `ReqCounterOffer` with the right id and count, the server
  confirms your side, the buttons collapse to Cancel. Accept stays hidden
  on a background offer, as the reference does.
- Stat change: the steppers respect the pool, OK validates intellect
  against the schools, the confirmation is an in-page modal, and Yes
  sends `ChangedStats` (157).
- Chat flood: 205 lines, the full log scrolls and closes.
- Resting: Rest raises the RESTING banner, Stand clears it. PARALYZED
  and SAVING photographed too, through the two new fixture switches.
- Mail: New, a recipient typed in, Send - `ReqLookupNames` validates the
  name first and `SendMail` follows.
- NPC quests: `@obj:Alice` then Quest lists her three, with description
  and instructions, and Continue sends `ReqTriggerQuest`.
- Skills: the Skills tab, row tapped twice, `ReqPerform` on the wire.
- Autorun: the Auto button walks with nothing touching the screen, and
  a backward stick drag cancels it and releases the button.
- Casting: target something, open Spells, tap the row TWICE - the first
  tap describes, the second casts - and `ReqCast` with the spell's id
  reaches the wire. Rows are named `row<id>`, so `@name:row5002` gets
  there without knowing where the row sits.
- Walking: a held stick drag sends `ReqMove`, the minimap redraws, and a
  wall stops you instead of letting you through.
- Go: puts `ReqGo` on the wire, and with the fixture's second room the
  whole change happens - new walls, a new object list, a rebuilt map,
  the room id and name in the status line, and the target cleared.
  Targeting and three hits then kill the rat in the second room too,
  so the hit count really is per object and not per session.

Not yet played: posting to the news board (the book that stands in for
it sits too close to the avatar for `@obj:` to find a pixel of it), and
the guild commands beyond reading the roster.

See also: the fixture -> fake-server.md | the panels -> mobile-client.md

## Drags, and why they were missing
Tags: process, lessons | @drag:<from>><to>@<frames> presses, slides over ten frames, HOLDS, then lifts - without a hold the movement stick reports nothing and the one interaction a phone client is for was untestable

`@tap` could already be scripted. The movement stick and the look drag
are both holds: a stick that is put down and lifted in one frame reports
no direction at all, so walking, turning and looking had never once been
exercised by a scripted run, only by hand.

`Relative` matters as much as `Position`. The look half turns by the
DELTA, so a drag that sets only Position turns the camera once and then
sits still for the rest of the hold.

Proved with `@drag:270x1150>270x850@150` (walk forward: eight `ReqMove`
on the wire and 4,949 changed minimap pixels) and a 400-frame hold into
a wall, which does not pass through it.

A drag that starts on a button or a panel is eaten by that control and
never reaches the touch layer. The run then looks exactly like a client
that cannot walk: no movement, nothing on the wire, no error. The step
now names the control it would hit - "WARNING drag starts on 'hot3'
(Button)" - so that costs a line instead of an investigation. That
warning, not a remembered rectangle, is the authority on where the
world is: the client is landscape now and everything moved. A drag
from 500x700 at 2340x1080 walks (eight `ReqMove`), and the old
portrait numbers - y 250..1150 at 1080x1920 - are history.

See also: the touch layer -> TouchControls.cs | the client -> mobile-client.md

## An empty minimap is usually a big room, not a broken map
Tags: gotchas, lessons | The dial came up blank in the fixture's second room with 454 walls loaded; six presses of "-" and the walls were there

The zoom is saved per character and carried into the next run like the
hotbar is, so a run can start zoomed further in than the room wants.
`MiniMap.MappedWalls` and the `room ->` line say how many walls
survived the map-never filter, which is the number that matters - the
room's own wall count does not tell you whether the map has anything to
draw. 454 on the map and nothing on the dial is a window problem;
0 on the map would have been a filter problem.

See also: MiniMap.cs

## Tap a thing by name, not by guessing where it is
Tags: process, lessons | @obj:duskrat asks the renderer's own picker where the thing is and taps there - three runs were lost to taps that hit the floor

`GameView.ScreenPointOf` sweeps screen points through `Renderer.Pick`,
the same call a finger goes through: opaque texels only, never through
a wall, and the avatar skipped. So a test that taps the answer still
exercises the whole path; it is not a shortcut into `TargetID`.

The guesswork it replaces looked like this: a tap aimed at a player
sprite hit the floor, so no target was set, so the action row stayed
hidden, so the button under test did not exist and the run reported "no
button called Quest" - which reads as a missing button rather than a
missed tap.

See also: SceneShot.cs | the client -> ../GameView.cs

## Emitting Pressed does not press a toggle
Tags: gotchas, lessons | The bare-name step emitted the Pressed signal, which a ToggleMode button ignores - "pressed Auto" and the client did not move an inch

`Hit()` knew this and flipped `ButtonPressed` instead; the bare-name
branch did not go through `Hit()`. Every press in SceneShot does now.
A harness bug of this shape reads exactly like a dead feature, which
is the expensive kind.

See also: SceneShot.cs

## A run inherits the last run's hotbar
Tags: gotchas, process | HotbarStore saves to user://hotbar.cfg per character, so every scripted bind is still there next run - the slots accumulate across a testing session

Working as designed, and a trap for anyone reading a screenshot: after a
few runs that bind a spell, the hotbar shows eight seeded actions plus
one slot per bind, and `hot{N}` names shift with them. Delete
`user://hotbar.cfg` (under the Godot user data folder) for a clean start.

See also: HotbarStore.cs
