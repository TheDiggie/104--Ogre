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
`@type`, `@type:<Node>=<text>`, `@submit`, `@name:<Node>`, `@slot`,
`@drag:<from>><to>@<frames>`, `@sweep:<from>><to>@<frames>`,
`@state[:<Node>]`, `@obj:<name>`, `@wait:<frames>` (nothing at all,
for the idle numbers). `M59PROF=120` in the environment prints the
per-frame cost probe every 120 frames - see godot-ui.md, "Per-frame
cost in the Godot layer".

Fixture switches on the fake server: `M59_STATCHANGE=1`, `M59_NEWS=1`,
`M59_CHATFLOOD=1`, `M59_PARALYZE=1`, `M59_WAIT=1`, `M59_QUESTLOG=1`,
`M59_NPCQ=empty|changing`, `M59_NEWSROW=1`.

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
- NPC quests: Alice targeted (see `@obj:` below - she is too close to
  the landscape spawn to be picked until you back away), then Quest
  lists her three, with description and instructions, and Continue
  sends `ReqTriggerQuest`. With `M59_NPCQ=empty` the same press opens
  her window with no rows - "Quests (0)", the portrait, "Nothing to
  offer just now." - and with `M59_NPCQ=changing` plus a `quests` on
  the trigger the list rebuilds from three rows to two UNDER the open
  window, keeping the row that was being read selected.
- The quest log changing: with `M59_QUESTLOG=1` the log starts "No
  Active Quests" / "No Completed Quests", one Continue starts a quest
  and the server pushes the whole group 5 unasked, and a second
  Continue finishes it - the quest leaves the active half and comes
  back under "Completed Quests:".
- The news board: `M59_NEWSROW=1` puts the globe in the container, so
  Activate then `@hold:loot3104` sends `ReqLook 3104`, the server
  answers `LookNewsGroup` and NewsPanel comes up with its three
  headers. It had never been opened by a scripted run before.
- The password form: `@type:oldPassword=...` and its two fellows, then
  `@name:changePassword` - `ChangePassword` on the wire,
  `PasswordOKMessage` back, "Password changed successfully." on screen.
  Note the client checks the old password against the one it logged in
  with before sending anything, so it has to be the real one.
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
- Logging out: `Tester,@name:menuButton,@name:logoutButton,Yes` and the
  whole round trip happens on one socket - `ReqQuit` out, `Quit` back,
  `GetChoice`, `ReqGameState`, `GameState`, `Characters`, picker. No
  password is typed and nothing reconnects. Pressing `Tester` again
  walks straight back into barinn, and the two in-world frames stack
  identically (one real bug came out of that: the avatar portrait never
  came back, `AvatarPanel.Follow`'s cache hit returning without
  re-showing the button). The fixture had to learn `ReqQuit` first -
  `inGame = false` plus a fresh `GetChoice`, which is what Server-104's
  `SetSessionState(STATE_SYNCHED)` amounts to on the wire.
- Go: puts `ReqGo` on the wire, and with the fixture's second room the
  whole change happens - new walls, a new object list, a rebuilt map,
  the room id and name in the status line, and the target cleared.
  Targeting and three hits then kill the rat in the second room too,
  so the hit count really is per object and not per session.

Not yet played: POSTING to the news board (the window opens now, see
above; New/Reply/Delete have not been driven), and the guild commands
beyond reading the roster.

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

## @sweep: a shot of the camera while it is still moving
Tags: process, lessons | One InputEventScreenDrag per frame at a constant Relative, a numbered PNG on every one of them and no settle - 1.95M pixels change between consecutive frames where @drag's shots are pixel-identical

`@sweep:<from>><to>@<frames>` presses at the first point, slides to the
second over ten frames, then holds for `<frames>` frames emitting one
drag per frame with `Relative` fixed at the slide's own per-frame delta,
writing `<out>-sweepNN.png` on each. No settle: the shot is of that
frame, moving.

`Position` is pinned at the far end and `Relative` is kept non-zero on
purpose, because the two halves of the touch layer read different
fields - the stick takes its direction from `Position`, the look half
turns by the DELTA (`TouchControls.cs:150-153`). A sweep that advanced
`Position` as well would walk the finger off the stick in a few frames.

Measured, `@sweep:1500x540>1700x540@8` in barinn: 1,941,192 to 1,952,888
pixels differ between each pair of consecutive frames, out of 2,073,600.
The control is `@drag:1500x540>1700x540@60` with `--shots`, where the
step's shot and the end-of-run shot differ by 2,396 pixels inside
(60,93)-(387,244) - the debug overlay's clock and frame counter, and
nothing else. The world is identical: at every moment `@drag` can
photograph, the camera is at rest.

Keep `<frames>` small - eight to twenty. Every frame is a PNG write.

See also: the touch layer -> TouchControls.cs | SceneShot.cs

## @state: what a control IS, as against what a press reports
Tags: process, gotchas | name, class, Visible, IsVisibleInTree, global rect, modulate, mouse filter, Disabled and the caption - two agents built this privately and threw it away

`@state` dumps every Control in the tree, parents before children.
`@state:<text>` dumps the ones whose NODE NAME contains that text, which
is what a run chasing one button wants. Hidden nodes are listed too: "my
press did nothing" is usually answered by a node that is there and not
visible-in-tree, and a list that left those out could not say so.

A line reads:

    [SceneShot] state @Button@475 (Button) Visible=True InTree=True
      rect=(1391,593,110,48) modulate=(1, 1, 1, 1) filter=Stop
      Disabled=False text="Get (1)"

Three things it settles that nothing else could. Whether a press would
have done anything - `Disabled` is honoured by a finger and not by the
Pressed signal the harness emits. Which of two same-captioned buttons a
bare name would take - the dump is in tree order, which is the order
`FindButton` walks. And whether a caption has moved under a step.

It is also how the auto-generated names are found. Godot names a node
with no explicit name `@Button@475`, and those numbers shift between
builds, so read them per run rather than writing one into a scene.

See also: SceneShot.cs

## @type:<Node>=<text>: a form with more than one box
Tags: process, lessons | Plain @type fills the FIRST visible LineEdit, so the three-box password form could not be driven at all and only its wire had ever been exercised

`@type:oldPassword=rats` names the box and the text; the text may
contain anything but a comma, which separates the steps. The miss
report lists the visible boxes by name, as the button steps do.
`TextChanged` is emitted after the assignment, because setting `Text`
in code raises nothing and a form that validates as you type would
never see the characters.

Plain `@type` is unchanged and still fills the first visible box, which
is what the one-box forms want.

The whole change-password form runs from a scripted run now:

    @name:menuButton,@name:settingsButton,
    @type:oldPassword=tester,@type:newPassword=lantern,
    @type:confirmPassword=lantern,@name:changePassword

with `M59_PASSWORD=ok` on the fixture - `ChangePassword` on the wire,
`PasswordOKMessage` back, "Password changed successfully." on screen.
One trap, found by driving it: `OptionsPanel.Rotate` compares the old
box against the password the client logged in with and refuses locally
before anything goes out, so the old box has to carry the real
`M59PASS` or the run reports a working form as a dead button.

See also: the options panel -> mobile-client.md | fake-server.md

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
Tags: process, lessons | @obj:duskrat asks the renderer's own picker where the thing is and taps there - three runs were lost to taps that hit the floor; and in landscape @obj:Alice needs a step backwards first

An earlier version of this note said `@obj:Alice` reaches her from the
spawn. It does not, and two more runs went into finding that out. What
it actually does now, measured: from the landscape spawn
`@obj:duskrat` finds the rat at (860,630) and `@obj:Alice`,
`@obj:Boris` and `@obj:a notice board` all print "nothing called ... is
visible from here". Turning does not help - eight 25-frame look drags
through a full circle, probed after each, found her at no angle. She is
48 units from the spawn against the rat's 64, and nothing closer than
the rat has a texel in the pick buffer; the globe and the board are
nearer still, which is the same reason the news window needed a fixture
route rather than a cleverer tap.

A step BACKWARDS fixes it:

    @drag:500x500>500x800@120,@obj:Alice

and she is tapped at (960,690). That is the opening of every NPC-quest
run in this file now. Next does not substitute: `DataController.
NextTarget` only considers objects that are attackable or minimap
enemies (`DataController.cs:1326-1340`), and Alice is neither, so no
number of Next presses ever reaches her.

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

A private resource dir per run is 123MB (`/tmp/res` copied), and the
agents' scratchpad is one shared root: by the dock's first run it held
19GB of stale `res*`/`xdg*` copies and the disk was full, which shows
up as the TOOL failing ("temp filesystem is full"), not the run.
Delete the copy when the run ends (`kill $SPID; rm -rf $RES`) - the
shots and the logs are the evidence, the copy is not.

See also: the fake server -> ./fake-server.md | delivery -> ./delivery.md

## Nine ways a scripted run lies about a working feature
Tags: gotchas, lessons | Each of these produced a screenshot or a log that looked exactly like a client bug, and each cost an agent a run or more

- `--shots` breaks any gesture with a time window. On top of the 30-frame
  settle every step already pays, it adds a draw wait and a PNG write per
  step (`SceneShot.cs:626-641`), enough wall-clock to outrun the spell
  book's 600ms double-tap (`SpellsPanel.cs:268,283`): the second tap is
  read as a first, the row only describes, and it looks like a dead cast
  path. Run timed-gesture tests without `--shots`.
- A bare-name `--press` step matches a button by its TEXT
  (`FindButton`), first in tree order, and that is wrong in two
  different ways.

  The first is a caption that exists twice. "Close" is on more than
  twenty buttons in this client - an `@state` dump counts them - and
  the step takes whichever comes first in tree order, which is not
  the one on top: a shop opened over the Acts panel is closed by a
  `Close` step that shuts Acts instead, and the shop then looks like a
  window that cannot be dismissed. Panels.ToFront decides what is on
  top and tree order does not follow it. Use `@name:<Node>` and name
  the button (`lookClose`, `LookPanel.cs:235`).

  The second is a caption that CHANGES. Loot's Get is "Get" until a row
  is ticked and "Get (1)" from then on, so the obvious sequence -
  `Loot,@name:loot3102,Get` - reports "no button called Get" and reads
  exactly like a dead control on a window that is working perfectly.
  Measured: with the row ticked, `@state` says
  `@Button@475 (Button) Visible=True InTree=True Disabled=False
  text="Get (1)"`. Any caption carrying a count or a state does this.
  Name the node, or read the caption with `@state` first.
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
- `@drag` cannot photograph motion, and for a long time nothing could.
  Each step settles 30 frames before it shoots, and `@drag` slides for
  ten frames and then HOLDS STILL (`now = to`, so `Relative` is zero)
  before it lifts, so the only photographable frames were frames on
  which the camera was not moving - which is exactly when a label
  placed with the previous frame's camera looks correct
  (`GameView.cs:2968-2985`). Two agents each rebuilt the way round it
  in a scratch copy and threw it away. It is `@sweep` now, in the
  repo; see its own section below. Use `@drag` for a hold with a
  settled shot at the end and `@sweep` when the bug only shows while
  the camera is turning.
- A press proves the node was FOUND, not that it could be pressed.
  Every press goes through `Hit`, which emits `Pressed` (or flips a
  toggle), and a Godot Button with `Disabled` set honours that signal -
  so "pressed Get" is printed for a greyed-out control a finger could
  not touch, and the feature behind it looks broken. Visible is no
  guard either: the hidden target-row Get reads `Visible=True
  InTree=False` while the loot window's reads `InTree=True
  Disabled=True`, both at once, in the same dump. `@state` is the
  answer and is why it exists - read Disabled and IsVisibleInTree
  before believing a press.
- A timed fixture event counts client messages, not milliseconds, and
  pings outpace frames under load, so a `--shots` run needs many dummy
  `--press` steps or the event fires before frame 1. The switches and
  the rule are in fake-server.md.

See also: the fixture -> fake-server.md | SceneShot.cs

## A fixture that answers more nicely than the real thing cannot fail

The fake server named the condition resources "health", "mana",
"vigor", "toughness". Those are not what a condition's resource
string is: it is the BGF FILENAME the library looks the icon up by
(`StatNumeric.cs:304-316`, `GetObject(resourceName)`). A real server
sends "icon.bgf".

The client was printing that string next to the bar instead of
drawing the icon the reference draws (`UIAvatar.cpp:396-405`,
`Resource->Frames[0]`). Against the fixture the bars read
"health / mana / vigor / toughness" and looked perfect in every
screenshot taken for months. On the player's phone the health bar
said "icon.bgf".

The fixture now sends filenames, and deliberately sends four
different shapes: two with art that exists, one whose art is missing,
and the literal "icon.bgf". All four fallback paths are on screen in
one frame.

The rule: when a fixture invents a value, invent it in the SHAPE the
real thing uses, even when a prettier value would make the
screenshots nicer. A pretty value is a check that cannot fail.

## A caption laid out for the player is not a string to match on

`--press` finds a button by its text. "Target Next" is written with a
hard line break so the two words sit inside a round seat, and the
moment that break went in the button became unpressable from a
script - the harness reported "no button called Target Next" while
the control was on screen and working.

FindButton compares with whitespace flattened now. The general shape:
a check that matches on something laid out for a human will break
when the layout changes, and it breaks SILENTLY, reporting the
feature missing rather than the matcher wrong.
