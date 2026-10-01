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

- Every panel opens and closes. They come out of the menu drawer now, so
  a run presses `@name:menuButton` first and the tile after it; a tile
  is not visible-in-tree with the drawer shut, so `FindButton` will not
  find "Bag" on its own. Map is a toggle with no Close, which is right,
  and Map, Auto, Say and Log are pinned outside the drawer and press
  directly.
- Chat: plain text sends `SayTo` with ChatTransmissionType.Normal and the
  room answers.
- Targeting: tap outlines in red, tapping elsewhere clears it, and the
  library resets TargetID itself when the target leaves the room.
- Attacking: three hits, `Remove`, the object goes and the outline with it.
- Buying: `ReqBuyItems` then `InventoryAdd`; a stackable carries its
  count, a single item sends x0. Two stacks of one item do NOT merge, and
  that is the game - hence `NumOfSameName`.
- Looting: `ReqGet`, the item lands in the pack and leaves the floor. An
  earlier version of this line said pressing Loot again opens nothing;
  that was a bug, not a fact - the emptied list kept its old rows
  (`LootPanel.cs:239,307-311`) - and the second open now shows "Loot (0)".
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
- Next target: two presses walk Boris the Outlaw then the duskrat,
  guild enemy first, as the library orders them.
- Actions: the Acts panel lists all eleven, Dance sends an `Action`,
  and binding Point adds a ninth hotbar button.
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

## A build that failed still prints "Time Elapsed"
Tags: gotchas, lessons | `dotnet build | tail -1` hides the errors above it, and Godot then runs the last assembly that DID compile

The symptom is the worst kind: new code that never runs, with no
error anywhere, on a client that otherwise works perfectly - because
it IS the previous build. Two hours went into "the projectile is not
drawn" before `strings` on the DLL showed the new method was not in
it. Grep the build output for `error CS`, or read the count line, and
never trust the last line alone.

See also: fake-server.md

## A run inherits the last run's hotbar
Tags: gotchas, process | HotbarStore saves to user://hotbar.cfg per character, so every scripted bind is still there next run - the slots accumulate across a testing session

Working as designed, and a trap for anyone reading a screenshot: after a
few runs that bind a spell, the hotbar shows eight seeded actions plus
one slot per bind, and `hot{N}` names shift with them. Delete
`user://hotbar.cfg` (under the Godot user data folder) for a clean start -
in the container that is
`/root/.local/share/godot/app_userdata/Meridian 59 Mobile/hotbar.cfg`
(`HotbarStore.cs:39`) - and do it per run, not per session: the phantom
buttons are a screenshot that looks like a layout bug.

See also: HotbarStore.cs

## Running it headless in the container
Tags: process, gotchas | Godot 4.7.2 mono under xvfb-run works; two environment traps and one fake-server rule cost a run each before they are known

A mono build of Godot 4.7.2 unzips from `/tmp/godot472.zip` and
`xvfb-run` drives it (it falls back to OpenGL on llvmpipe). With
`Tools/Meridian59.Net8FakeServer` on a port and `SceneShot.tscn --host
127.0.0.1 --char Tester` the client reaches the world reliably, and
`UiShot.tscn`, `WeatherShot.tscn` and `Tools/Meridian59.Net8Fpv` all run
the same way.

`Directory.GetCurrentDirectory()` inside Godot is NOT the shell's
working directory, so the last-resort lookups that find
`Resources/roomtextures` and `Resources/sky` relative to the repo find
nothing. Set `M59ROOMTEX` and `M59SKY` explicitly or the replacement
textures and the sky silently do not appear - identical frames with and
without, which reads as "the feature does not work".

The resource folder must be `/tmp/res`. `M59Paths.HasContent` wants
`.roo` or `.bgf` at the TOP level, so a dump whose files sit in
subfolders is rejected and the client lands on the "no resource folder
found" screen.

The fake server accepts ONE client per process, and its session extras -
the trade offer, paralyze, blind, pain, whiteout, invert - are gated on
a static set that fills on the first connection. A second run against
the same process looks like half the fixtures are broken. Restart it
per case.

See also: the fake server -> ./fake-server.md | delivery -> ./delivery.md

## Seven ways a scripted run lies about a working feature
Tags: gotchas, lessons | Each of these produced a screenshot or a log that looked exactly like a client bug, and each cost an agent a run or more

- `--shots` breaks any gesture with a time window. On top of the 30-frame
  settle every step already pays, it adds a draw wait and a PNG write per
  step (`SceneShot.cs:626-641`), enough wall-clock to outrun the spell
  book's 600ms double-tap (`SpellsPanel.cs:268,283`): the second tap is
  read as a first, the row only describes, and it looks like a dead cast
  path. Run timed-gesture tests without `--shots`.
- A bare-name `--press` step matches a button by its TEXT
  (`FindButton`, `SceneShot.cs:759`), first in tree order. A caption that
  exists in two open panels hits the wrong one: "Close" on a look window
  pressed the spell book's Close behind it, and the shot looked exactly
  like the bug being chased. Use `@name:<Node>` and name the button
  (`lookClose`, `LookPanel.cs:235`).
- `@obj:` taps wherever the picker finds the object, and the left half of
  the screen belongs to the movement stick (`TouchControls.cs:8-9`), so a
  touch there can never become a target. It prints "tapped" and sets
  nothing. To acquire a target in a scripted run press Next
  (`GameView.cs:1926`).
- Every fake server rewrites `rsc0000.rsb` in its resource dir
  (`Program.cs:366`), so parallel runs sharing one dir clobber each
  other. Give each run its own resource dir and its own port: server
  args are `<port> <dir> <room>`, the client's are `--port` and `--res`
  (`SceneShot.cs:26,49`). The `/tmp/res` rule above is for a single run.
  Agents also share the scratchpad root and `user://`: `hotbar.cfg`
  (`HotbarStore.cs:39`), `character.cfg` (`GameView.cs:1001`) and the
  unpacked resource folder all live under Godot's per-user data folder,
  so give each run a private subfolder AND a private `XDG_DATA_HOME`.
  And kill a server by the PID you saved when you started it: a
  `pkill -f <pattern>` whose pattern is in your own command line kills
  your own shell (it happened).
- A scripted DOUBLE TAP cannot be made. Every step ends with a 30-frame
  settle (`SceneShot.cs:627-628`), so the gap between two steps is
  30 frames divided by the frame rate: 2-3 seconds under llvmpipe, where
  agents measured 9-18 fps. The bag's window is 250ms
  (`InventoryPanel.cs:144,555`), which 30 frames only fit at 120 fps or
  better; the spell book's 600ms (`SpellsPanel.cs:268`) fits only
  above 50. So `Bag,@slot,@slot` puts nothing on the wire while
  `Bag,@slot,Use` sends `ReqUse` (`InventoryPanel.cs:189`), and the
  bag's double-tap-to-use and the spell book's double-tap-to-cast read
  as dead features. Select, then press the button; for a cast the
  `--shots`-free run above works only on a fast machine.
- Continuous motion cannot be photographed with the stock steps, and
  that is a limit of the steps, not of the client. Each step settles 30
  frames before it shoots, and `@drag` slides for ten frames and then
  HOLDS STILL (`now = to`, so `Relative` is zero, `SceneShot.cs:490-505`)
  before it lifts. The only photographable frames are therefore frames
  on which the camera was not moving - which is exactly when a label
  placed with the previous frame's camera looks correct
  (`GameView.cs:2968-2985`). An agent got through it in a scratchpad
  copy of SceneShot (NOT in the repo) with a step that emits one
  `InputEventScreenDrag` per frame with a constant `Relative` and shoots
  mid-hold with no settle; the look half turns by the delta and ignores
  `Position` (`TouchControls.cs:150-153`). Write that step again when a
  bug only shows while the camera moves; do not conclude it cannot be
  done.
- A timed fixture event counts client messages, not milliseconds, and
  pings outpace frames under load, so a `--shots` run needs many dummy
  `--press` steps or the event fires before frame 1. The switches and
  the rule are in fake-server.md.

See also: the fixture -> fake-server.md | SceneShot.cs
