# The mobile client

`MobileClient/` - Godot 4.7, C#, eleven panels, built to reference parity
with `Meridian59.Ogre.Client` and then played until it was usable.

## The panels
Tags: architecture | Each panel owns its opener button and registers it with Panels.Opener, which now decides WHERE it goes as well as whether it shows

Chat, spells/skills, inventory, loot, buy, trade, look, amount prompt,
hotbar, minimap, lost-connection. `GameView.cs` gates all of it on
`inWorld = _wasInGame || _client.Data?.AvatarObject != null` - gating on
`EnteredGame` instead left a newly created character in a room with no
interface at all, because character creation skips `UseCharacter`.

The openers were a row of fourteen small text buttons along the bottom
edge, each panel counting its own seat from the right. They are a
DRAWER now: one `Menu` control at the top centre opens a card of large
labelled tiles, and `Panels` lays them all out from one place. Three
things stay off the grid because they are constant and are not combat -
Say and Log in the chat's own line, Map beside Menu, and Auto on the
left edge by the thumb that steers. The bottom-left is the movement
stick and the bottom-right is the combat cluster; nothing else goes
there.

See also: godot-ui.md | Panels.cs | MenuDrawer.cs

## The hotbar persists per character
Tags: architecture | HotbarStore writes Num, ButtonType, NumOfSameName and Name only - Data is re-resolved by name by DataController, and Label is a key binding a phone does not have

An item is matched by name AND NumOfSameName, because names in this game
are not unique. A stored object id would be worse than useless: ids are
per session.

Unset rows are written and read back too, in Num order, as the game
writes every cell (`OgreClientConfig.cpp:1203-1211`): a seat the player
emptied stays empty across a login instead of every later binding
moving up one. See "The HotKeys panel" for why that matters now.

See also: HotbarStore.cs

## The HotKeys panel
Tags: design | Hotbar pages laid flat - a page a row of HotSeats seats - for putting a binding in a chosen seat and moving one; the two things the "+" buttons cannot do

`HotKeysPanel` is the drawer tile "HotKeys" ("Hotbar pages"), beside
Actions. The reference needs no such window: all 48 cells are on
screen and a spell is DRAGGED onto the one you want
(`UIActionButtons.cpp:359-438`). Here the list you drag from covers
the arc, so Bind takes the first empty seat, and until this panel
nothing could choose the seat or move a binding afterwards.

The rows are the cluster's own model: every config but the primary,
in Num order, cut into pages of `ActionButtons.HotSeats` (six since
2026-10-03; the card's width is read off HotSeats too, because at six
a row no longer fit the list measure and the last seat ran under the
scrollbar). The
primary is shown once at the top and is not a seat; it CAN be Set
(see "The combat cluster: Phase, Door inside, the padlock and a
chosen primary" below), never cleared or moved; unbound, the row
offers "Set Attack". Tap a seat,
then the footer strip: Set… opens an in-panel chooser (Actions as
`ActionsPanel` lists them plus Go, Spells, active Skills, Items you
carry) and writes the seat with the library's own setters
(`ActionButtonConfig.SetToSpell/SetToSkill/SetToItem/SetToAction`,
`ActionButtons.SetToGo`), keeping the seat's Num; Clear is
`SetToUnset`; ◀ ▶ SWAP THE TWO NUMS of the seat and its neighbour,
across a page boundary too. Nothing else moves: the configs, their
listeners and BaseClient's subscriptions to them stay where they are,
and the cluster re-sorts by Num. Add page appends empty seats to one
page past the last; Remove last page is offered only for an empty one.
Every change is `HotbarStore.Save` and the cluster rebuilds off its
signature at once (`ActionButtons.WantPage` turns it to the edited
page when it comes back).

Two things changed under the cluster for it. The arc is POSITIONAL:
an Unset config keeps its seat and draws as the ring, where it used
to be compacted out - the reference's grid shows every cell and a
clear (`:471-473`) empties one without moving the rest. And the arc
is ordered by Num rather than list position (`ActionButtons.ByNum`,
`Stable`), which is what makes a swap of Nums a move. The store writes
Unset rows now so a hole survives a login.

Played, with `--shots`: open with two pages; `hk7` (page 2, seat 4)
Set… > Spells > `pick5002` puts kraanan's blessing there; ◀ swaps it
with Wave; `hk1` Clear leaves a ring where Loot was; Close shows the
ring on page 1 of the cluster; relaunch on the same `XDG_DATA_HOME`
shows the same rows, and `hotpage` shows Buy, Trade, spell, Wave. Five
Add pages scroll the list behind the gutter; Remove last page takes
one back. Seats are named `hk{pos}` (zero-based, across pages), the
chooser rows `pick{id}` / `pick{Action}` / `pickGo`, the tabs
`hkTab{Actions,Spells,Skills,Items}`, the strip `hkSet`, `hkClear`,
`hkLeft`, `hkRight`, `hkAddPage`, `hkRemovePage`, `hkClose`, `hkBack`.

See also: HotKeysPanel.cs | the hotbar -> ActionButtons.cs | HotbarStore.cs

## The combat cluster: two arcs, the padlock and a chosen primary
Tags: design, architecture | Six bindings on the outer arc, a page; four fixed utilities (Next, Phase, pager, Door) on an inner arc at r 168; a padlock (default locked, global, in hotbar.cfg) gates the pull-off clear; the big disc is any config, chosen by Num (`primary=` in hotbar.cfg), and is never cleared

Ashton (2026-10-03, a marked-up phone shot): a normal-sized button that
casts Phase ("every player has it and it is very important"); one more
seat on the arc with Door moved right; no dragging off Attack, Phase
or Door; a padlock bottom-right that locks the hotkeys; and the
customizer able to put other things on the big attack button. Later
the same day: "move the target next button down a bit and add another
hotkey to the hotkey arc. I want an outer hotkey arc of 6 keys and the
inner arc of utilities 'door, page change, phase, target next'".

- THE OUTER ARC IS SIX BINDINGS, radius and chord unchanged
  (`ActionButtons.ArcSeats`, `ArcR` 300, 70 to 180 degrees):
  `HotSeats = ArcSeats`, a page is six, and a hotbar.cfg saved under
  five re-pages (Nums are one long row; the page is a window on it).
  Bindings still fill bottom-left to top (`ArcSeat(v,i) = Seat(v,
  HotSeats-1-i)`), so the arc reads as the HotKeys panel's row.
  Ceiling and the cluster's box did not move: the top seat is where
  Target Next was.
- THE INNER ARC, four fixed controls at 1x radius `InnerR` 168 from
  the pivot, scaled with it, from the top going round: Target Next
  60 deg, Phase 102, the pager 142 (its smaller 72 size), Door 180
  beside Attack. Chosen for the chords: 168 is 40 off the disc's rim
  and 36 off the outer seats' inner rims (a radial gap is a floor on
  the true one); the 42/40/38-degree steps leave next-to-phase 24,
  phase-to-page 31, page-to-door 25. Nearest outer seat: next 42,
  phase 42, page 50, door 36. Next's right rim is 28 from the glass.
  `Measure` prints all of these and counts `under44`, `overlaps` over
  every control (circles, and the lock's square) and `offglass`; the
  proof is `controls=12 under44=0 overlaps=0 offglass=0` at 1920x1080
  and 2400x1080 (box 499x428, ceiling 566, unchanged).
- PHASE is a fixed control like Door, not a config: never paged,
  cleared or saved. It finds the player's spell by name
  (`ActionButtons.FindPhase`, "phase", case-insensitive, on every
  Sync because names resolve late) and sends it through
  `CastSend` = `_client.SendReqCastMessage(SpellObject)`
  (`BaseClient.cs:1717`) - the method the spell book's cast reaches
  (`GameView` wires `_book.Cast` to the uint overload at `:1702`,
  which calls it at `:1709`) and a Spell seat's `Activate` reaches
  (`OnActionButtonActivated`, `:284-285`). Through `Run`, so gated and
  latched like a seat. Shows the spell's own icon with "Phase" under
  it; without the spell it is dimmed (modulate 0.45, Disabled) and the
  press is guarded for the harness's emitted Pressed too. The fixture
  gives it as `M59_PHASE=1`: spell 5003 "phase", `iphase.bgf` (in the
  dump), ZERO targets as the live spell has - with the fixture's
  default of one the library sends nothing untargeted
  (`BaseClient.cs:1719-1746`), which looked like a dead seat.
- THE PADLOCK (`hotlock`, 44x44 at the disc's bottom-right, 16 from
  the glass, 26 from the disc's rim at its nearest; a drawn glyph,
  `ActionButtons.LockGlyph`, open and gold when unlocked). Locked, the
  pull-off clear (`OnUp`) does nothing on any seat; everything else
  works. `HotbarStore.Locked`, `[client] locked=` in hotbar.cfg -
  global, not per character or layout, and not in settings.cfg because
  `OptionsPanel.Keep` rewrites that file whole. DEFAULT LOCKED. The
  disc is never pulled off, locked or not; Door, Phase and Next have
  no pull-off path at all. The hotkey box (HotkeyDock) has no clear
  gesture, so nothing to gate there.
- THE PRIMARY IS A NUM. `ActionButtons.PrimaryNum` (-1 = the seat
  holding Attack, the old rule) and `ActionButtons.Primary(data)` is
  the one resolver the cluster, the HotKeys panel and its chooser use.
  The panel's Primary row seat (`hkPrimary`) is pressable; Set… offers
  Actions (Attack included), Spells, Skills, Items; Put rewrites the
  primary's config IN PLACE with the library's setters and sets
  PrimaryNum to its Num, so nothing on the arc moves. Clear and ◀ ▶
  are disabled for it. Whatever the disc holds is withheld from the
  arc's chooser (same swing twice); Go is withheld from the disc
  (Door is beside it). `HotbarStore` writes `primary=<num>` in the
  character section; a file without it loads exactly as before. The
  disc's picture is composed at the disc's proportion (`IconPx *
  Atk/Btn`), or a 56-pixel sprite is a smudge in a 160 circle. Hold-
  repeat stays `Repeats` by what the disc holds (a spell on the disc
  re-casts while leant on, as a spell seat does; the library's
  CanReqCast is the cadence).
- HARNESS: `@pull:<Node>` is the clear gesture (down, a drag past
  PullOffPx, up, no Pressed). Pooled seat buttons are renamed
  `pool{i}` at every rebuild, or a `hot{Num}` left on a hidden one
  made Godot rename the newcomer (`hot5` came out `hot6`).

Played, `--shots`, fresh `XDG_DATA_HOME`, `M59_PHASE=1`:
`@name:hotphase` -> `<- ReqCast id 5003`; `@pull:hot0` locked -> Rest
still there; `@name:hotlock` (chat: "Hotkeys unlocked…"), `@pull:hot0`
-> ring, "Rest cleared from the hotbar."; `@pull:hot1` (the disc),
`@pull:hotdoor`, `@pull:hotphase` -> nothing changes; Menu > HotKeys >
`hkPrimary` > `hkSet` > `hkTabSpells` > `pick5002` > `hkClose` -> the
disc shows kraanan's blessing, `Target Next,@name:hot1` ->
`<- ReqCast id 5002`; hotbar.cfg reads `locked=false`, row 0 Unset,
row 1 Spell, `primary=1`. Relaunch at `--resolution 2400x1080` ->
same disc, open padlock, same rects right-anchored, `@pull:hot2`
clears Loot, `@name:hotlock` -> `locked=true`. An old-format file
(nine rows, no `primary`, no `[client]`) -> Attack is the disc, two
pages of five with the hole kept, locked by default, `@pull:hot0`
does nothing. Without `M59_PHASE` the seat reads "Phase" at 0.45
and its press puts nothing on the wire.

See also: ActionButtons.cs | HotKeysPanel.cs | HotbarStore.cs | the fixture -> fake-server.md | harness.md

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

## The game data comes from the website now
Tags: architecture, process | The Android APK no longer carries res://resource; ResourceSync fetches a manifest every launch and downloads only what changed - two paths, decided once in GameView.Boot

Half a gigabyte of game data inside every APK meant every change to
the data was a new build to sideload, and a player two builds behind
re-downloaded files that had not changed. So the Android export leaves
`res://resource` out (`export_presets.cfg`), and `ResourceSync.cs`
fetches it instead: the website publishes `resources.json` - a stamp,
and one line per file with its size and SHA-1 - and the client fetches
that on every launch, diffs it against `user://resource`, and fetches
only the files that are missing, the wrong size, or carry a different
hash. The first launch fetches everything; every later one fetches a
manifest and nothing else.

Two paths, and which one is a property of the BUILD. `GameView.Boot`
decides once:

- `res://resource` exists (the desktop, or an APK exported with it):
  the unpack path above, unchanged.
- Otherwise `M59Paths.Resolve` is asked, and what it finds is asked
  whose it is. `--res`, the saved path, an installed client's folder:
  not ours, straight to the game, nothing fetched. `user://resource`:
  ours, and it goes to the sync every launch WHATEVER is in it
  (`ResourceSync.Owns`). Nothing found: the sync.

That last rule was learned the way the unpack marker was. The first
cut owned the folder by marker - the sync's record, or `.unpacked` -
and a first download that died before the record was written left a
folder with no marker and a thousand `.bgf` files, `HasContent` was
satisfied by one of them, and the client played a room whose `.roo`
was ten bytes long. Ownership is by path now; whether the folder is
COMPLETE is a separate question and only the record answers it.

What the sync keeps and does:

- `user://resource/.synced.json` is the last manifest fully applied,
  written after the last file and nowhere else. Absent means nothing
  is known. A crash mid-run leaves the old record (or none) and some
  `.part` files; the next launch sweeps the `.part`s, hashes any file
  at the right size that has no record, and fetches only what is
  missing or wrong - measured: no record and one truncated file, 1057
  hashed and kept, 1 fetched.
- The fast path: remote stamp equals the recorded one, one stat per
  file, nothing hashed. "Game data is up to date (1,058 files)" on the
  login card and in the chat, the way the update check's verdict gets
  there - `GameView.Heard` holds both lines now, because the second to
  arrive used to erase the first.
- A file goes to `<name>.part`, is hashed as it streams, and is renamed
  over the real name only if the hash matches; three tries with a
  growing pause, and a file that still fails is a Problem screen naming
  it, with Try again. Free space is checked before the first byte
  (`M59Paths.Headroom`, shared).
- Files the manifest does not name are deleted, subfolders included -
  only on this path, where the folder is ours. The two dotfiles stay.
- The first run asks. Nothing of the manifest on the device: the
  unpack screen's card says the size and the count with Download and
  Not now; Not now leaves the card up with Download on it, never a
  blank. Any later run - a resume, an update - never asks.
- Offline: the manifest cannot be fetched and the record says the set
  is complete and every file is at its length, play, with "Offline -
  playing with the game data you have." on the card. No complete set:
  a Problem naming which kind of failure - unreachable, 404, or a file
  that will not parse - as `Updater.Answered` does.

`HttpClient`, not Godot's `HttpRequest` node: the node is one request
at a time, lives in the scene tree and answers on the main thread; this
is thousands of requests from a worker. One instance for the run.

The harness recipe. A manifest from the test set, served beside it:

    # gen.py: top level of /tmp/res, sha1 each, the contract's JSON
    cd /tmp/ressrv && ln -s /tmp/res resources
    python3 gen.py /tmp/res http://127.0.0.1:8078/resources/ > resources.json
    python3 -m http.server 8078 --bind 127.0.0.1

Start the fake server BEFORE generating the manifest - it rewrites
`rsc0000.rsb` in its resource dir (`Program.cs:727`) and a manifest
made first describes a file the client can no longer fetch unchanged.
Then the client with a fresh `XDG_DATA_HOME`, `M59RESOURCES` at the
manifest, and `--res` pointed at a folder that does NOT exist -
SceneShot's default is `/tmp/res`, which Resolve would find and send
straight to the game:

    XDG_DATA_HOME=/tmp/synctest/xdg M59RESOURCES=http://127.0.0.1:8078/resources.json \
      xvfb-run -a $GODOT --path MobileClient --headless=false res://SceneShot.tscn -- \
      --host 127.0.0.1 --port 15998 --char Tester --res /tmp/none \
      --press "@state:zz,@name:downloadLater,@name:downloadNow,@state:zz,..." --shots

The buttons are `downloadNow`, `downloadLater` and `tryAgain`. Pad with
`@state:zz` steps: the card is up in a frame or two but the frames
are fast while nothing is rendered, and a run with too few steps exits
mid-download - which is what the crash-resume test is made of. What
was run, in order, each against a restarted fake server: fresh dir,
Not now, Download, 1058 files at matching SHA-1s and the record
written, then the world; the same dir again, one manifest GET and
nothing else; a truncated local file plus a changed served file,
exactly those two fetched; a junk file, a junk subfolder and a stray
`.part`, all three gone; the http server stopped, the offline line and
the world; a fresh dir against a 404, the Problem card and Try again
refetching; `--res /tmp/res`, no sync at all.

See also: ResourceSync.cs | the screen -> UnpackScreen.cs | the updater -> Updater.cs

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
has to be its own control. It is a tile in the menu drawer,
registered with `Panels.Opener`, which places it.

It used to sit in the bottom row between Log and Settings, placed by
slot arithmetic that drew it on top of Settings - 96 wide where every
other slot was 76, and the shot read "SetGoings". That is the reason
no file counts seats any more.

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

It also keeps walking with a panel open. The reference exempts it from
the UI's input lock (`if (isAutoMove || ...)` runs `TryMove` at
`ControllerInput.cpp:934-951`, the lock's early-out only at `:957`);
the panel-up branch here used to `Settle` and stop dead, so opening the
bag mid-crossing halted you (1 `ReqMove` after the Bag press, 33 once
fixed). The branch calls `TryMove` forward while autorun is on and the
server is not making you wait, and still does not read the stick, so a
drag on a panel neither walks nor cancels (`GameView.cs:3096-3104`).

See also: godot-ui.md | the harness -> harness.md

## Two control switches: move by pad or touch, look by stick or touch
Tags: design, architecture | Moving and looking are chosen separately (M59Hud.MovePad, M59Hud.LookStick), both part of the saved HUD layout; the glass keeps whichever job the fixed pieces did not take, over its whole width

Ashton, first round: "some people don't like the touch-anywhere
controls" - which became one scheme switch, Touch anywhere or Fixed
pad + stick. Second round: "some people don't like using the joystick
to look around, they said it's sluggish. Let people use the joystick
to move but touch anywhere to look. Make both joysticks optional." So
the pair is split into two switches, and all four combinations work:

| Move   | Look  | Left-half drag | Right-half drag | Pieces on glass |
|--------|-------|----------------|-----------------|-----------------|
| Touch  | Touch | floating stick | look drag       | none (default)  |
| Pad    | Touch | look drag      | look drag       | dpad            |
| Touch  | Stick | floating stick | floating stick  | lookstick       |
| Pad    | Stick | tap only       | tap only        | dpad, lookstick |

- With one job given to a fixed piece the glass has NO midline: the
  whole of it is the other job (`TouchControls.MoveTouch`/`LookTouch`,
  the MODES paragraph of its class comment). A finger that lands on
  the pad or the stick never reaches the glass (`FixedControls.Handle`
  runs first in `GameView._UnhandledInput`), and a second finger
  anywhere can only tap. Both off is the old TapOnly, now derived.
- Each piece is Live, drawn and claiming fingers exactly when its own
  switch is on (`FixedControls.ShowPad`/`ShowStick`, `Usable`), so the
  editor shows a handle for the pad alone when only the pad is chosen.
- Saved per slot in `user://hud.cfg` as `move=pad` and `look=stick`,
  each written only when on, so an untouched layout writes nothing.
  The old `controls=fixed` still reads as both on and is dropped on
  the next save (`M59Hud.ApplyControls`/`StoreControls`). Snapshot and
  Restore carry both, so the editor's Cancel backs them out.
- Two doors: the arrange screen's bar row three is two half-width
  toggles (`hudMove` "Move: Pad|Touch", `hudLook` "Look: Stick|Touch"),
  Settings > Controls has two Choice rows (`moreMove`/`lessMove`,
  `moreLook`/`lessLook`) that write the layout file at once. The
  options Close button is `optionsClose` now, so a run can shut it.
- The d-pad is the keyboard's touch form: W/S/A/D are forward, back,
  strafe left, strafe right (`OISKeyBinding.cpp:34-37`,
  `ControllerInput.cpp:681-704`), reported on `TouchControls.Move`'s
  axes and added in `GameView.ApplyInput`. The look stick is a RATE:
  deflection times `TurnSpeed` times the Look speed option, pitch at
  `PitchRate`, Invert look honoured, not halved by the walk modifier
  (`ControllerInput.cpp:968-972` is a rotate-key rule).
- The stick's rate, measured against the library: full deflection is
  `TurnSpeed` 3 rad/s = 172 deg/s at Look speed 10/30, and the
  reference's keyboard turn is `KEYROTATESPEED * KeyRotateSpeed *
  Span` = 0.00012 * 25 rad/ms = 3 rad/s (`ControllerInput.h:49`,
  `OgreClientConfig.h:61`, `ControllerInput.cpp:968`) - the same, so
  it was NOT raised. "Sluggish" is against the drag, not the keys: a
  swipe adds 0.006 rad/px (the reference's 0.000125*45 = 0.0056,
  `ControllerInput.cpp:447`, `OgreClientConfig.h:60`), so a 960 px
  half-width swipe is 330 degrees paid out at 41% a frame
  (`TouchControls.LookSpend`), which the stick takes two seconds to
  match. The Look speed slider already takes the stick to 9 rad/s at
  30/30; the answer to sluggish is the split above, which is what was
  asked for.

Played (harness, 1920x1080, `@sweep` on each half, counts off the
fixture log and frame-to-frame pixel diffs of the world strip):
Touch/Touch - right sweep 760k px a frame changing, left sweep draws
the floating ring at its origin and moves (18 `ReqTurn`, 11
`ReqMove`); Pad/Touch - both sweeps turn (28 `ReqTurn`, 0 `ReqMove`),
`dpad` InTree, `lookstick` not; Touch/Stick - both sweeps are the
floating stick and move (22 `ReqMove`, 0 `ReqTurn`), `lookstick`
InTree, `dpad` not; Pad/Stick - 0 px, 0 on the wire, both InTree. A
file holding only `controls=fixed` comes up with both; `move=pad`
written by the editor and `look=stick` by Settings survive a relaunch.
Note the sweep frames are named `out-<step>-sweepNN.png` now: two
sweeps in one run used to overwrite each other's eight frames.

See also: FixedControls.cs | TouchControls.cs | M59Hud.cs | HudEditor.cs | the harness -> harness.md

## The inventory dock: the pack on the glass
Tags: design, architecture | A HUD piece ("dock") that shows the pack over the world with the bag's four verbs on a tap; the arrange screen moves, scales, fades, hides it and sets how many ACROSS and how many DOWN - the box is Rows x Columns and a bigger pack scrolls inside it (TouchScroll + slim bar)

Ashton: "add an inventory window that I can use to always see and
interact with so I don't need to go through menus to see my items.
Make sure people can move, resize or close it in the UI customizer."
`InventoryDock.cs`. The full-screen bag is unchanged and still opens
from the drawer; the dock is in addition, the way the purse is in
addition to the coin in the bag.

- A piece like the purse: registered as "dock" / "Inventory dock",
  default VISIBLE (he asked for it to be there), at the left edge a
  gutter under the side keys' natural rect (Auto run) - at 1920x1080
  that is (16,543), eight 56-point slots across, clear of the target
  card (1336+), the combat arc, the chat block (830+) and the fixed
  pad (648+). Found from the `sidekeys` piece's NATURAL rect, not
  where the player moved it, for the reason FixedControls gives.
- "Resize" is Scale (the slot side follows it, 56 at 1x, which is a
  thumb plus room for the game's 40-in-52 icon), plus COLUMNS:
  `M59Hud.Piece.Columns` with `MinColumns/MaxColumns/DefaultColumns`
  set by the piece at Register. Only a piece that sets a range gets
  the "Across" row on the editor's card (`HudEditor.HasColumns`),
  steppers only - a dozen honest values, not a track. Saved as an
  OPTIONAL sixth field on the piece's line
  (`dock=852.63,-285.26,1.2,1,0,6`), so a file from before reads;
  carried by Snapshot/Restore so Cancel backs it out; Reset zeroes it.
  WHY a count and not a free rectangle: a grid's size is its contents
  wrapped, so a dragged rectangle would have to be reconciled with
  the slot size on every edge and would lie the moment the pack
  changed; the same slots wide or tall is one number.
- "Close" is Hide, the same verb as every piece (`hud.cfg`
  `dock=0,0,1,1,1`); the editor shows it faint so it can be found.
- A tap picks the item: gold ring, and a strip hung under the grid
  (over it when the grid is near the bottom) with the item's name,
  Use/Look/Hotbar from the left and Drop pinned to the far right, for
  M59Skin.FootLeft's reason. Same slot again, a tap on the world
  (`GameView._UnhandledInput`, on the press), a panel, the drawer or
  the editor (`Covered()` in Pump) puts it away. No double-tap-to-use:
  the strip is one tap away and a second tap on a small slot over the
  world is too easy to make by accident.
- The four verbs are the bag's own handlers, made methods so both
  subscribe (`GameView.UseFromPack/DropFromPack/LookFromPack/
  BindFromPack/TargetFromPack`): Drop of a stack opens the same
  AmountPrompt, a single thing drops at once. The picked item becomes
  `Data.TargetID` one double-tap window later as the bag's does, and
  the dock's selection COUNTS AS THE BAG BEING OPEN for the
  `_targetBeforeBag` save-and-restore in Pump - so putting the strip
  away gives the world target back, and Apply aims at it.
- Slots are `InventorySlot` with MouseFilter.Stop; the ground between
  them is Ignore. A drag that starts on a slot is eaten by it like a
  drag on any HUD button; one that starts beside it walks (measured:
  5 `ReqMove` from a stick drag with the dock on the glass).
- Icons through `InventoryPanel.ComposeIcon`, now static and shared
  with its own cache per view, and a miss is no longer cached by
  either - retried on a 500ms timer, the rule in godot-ui.md.

Played (harness): six items at (16,543); tap -> ring + strip, Look ->
`ReqLook 8001`; Drop of 240 shillings -> the amount prompt, OK ->
`ReqDrop ... count 240`; Drop of the axe -> `ReqDrop`; arrange: drag
to (868,257), Size +4 = 1.20x, Across -2 = 6, Done, relaunch on the
same user data -> there, and a tap there targets the axe; Hide, Done
-> `dockHost Visible=False`; drawer open -> dock drawn under it like
the purse, strip dropped; world tap -> strip gone.

One bug caught by looking: the first "-" on Across took the dock from
eight to TWO, because the editor started the count from the range's
bottom when the model held zero. Zero means the piece's default, and
only the piece knows it - hence `DefaultColumns` on the piece.

### Down, and the scroll (2026-10-02)
Ashton, with a phone shot of ninety items at seven across running off
the bottom through the chat: "add an adjustment for vertical as well.
If a player has more than the slots can show just add a slider."

- `M59Hud.Piece.Rows` with `MinRows/MaxRows/DefaultRows`, `RowsNow`,
  the same shape as Columns; dock range 1..8, DEFAULT 2. Why 2 and
  not the 1 a six-item pack drew before: at one row the box is 56
  tall and a grabber that must stay 44 has 12 points to travel - a
  bar that cannot be dragged. Two is the smallest box whose bar
  works. A six-item pack now shows 6 filled + 10 empty squares (the
  grid is padded to the BOX, `Rebuild`, so a three-row box with two
  items reads three rows tall); Down 1 gives the old strip back.
- Saved as the OPTIONAL SEVENTH field: `dock=0,0,1,1,0,0,4` - the
  sixth is written as 0 ("default") when only rows are set, because
  the seventh needs a sixth in front of it (`M59Hud.Save`). A 5- or
  6-field line still reads (`ApplySaved`), proved by hand-writing
  `dock=0,0,1,1,0,6` and launching: 6 across, 2 down.
- The box is a `TouchScroll` (`dockBox`) holding one `Control`
  (`dockGrid`) whose CustomMinimumSize is the whole pack, slots
  placed by hand inside it. Reused because it IS a ScrollContainer:
  drag-anywhere, flick with friction, the slop that keeps a tap a
  tap, clipping and the bar come for free, and the bag already runs
  `InventorySlot` under one so the tap-vs-scroll split was proven.
  Place gets the box rect (+28 for the bar when it scrolls), so the
  editor handle covers the box, Dress fades `dockHost` above it, the
  slot side still follows Scale. Not a SubViewport: a second render
  target for a dozen icons buys nothing a ScrollContainer's clip does
  not, and clipped slots still take taps (Godot's gui hit-test honours
  clip_contents) - a slot scrolled out of the box cannot be tapped.
- When the pack fits: no bar, VerticalScrollMode Disabled, the box is
  MouseFilter.Ignore (the gap between slots stays the world's) and
  the TouchScroll's input is OFF (`SetProcessInput(false)`). The
  gate matters because TouchScroll scrolls from `_Input`, before the
  GUI walk: left on it would eat a stick drag that began in the gap,
  scroll the dock under an open panel's list, and fight the editor's
  drag of the piece. So it is also off while `Covered` - one line in
  `GameView.Pump` sets `_dock.Covered = Covered()` beside the
  Deselect it already did - and while `M59Hud.Editing`.
- `InventorySlot.CanDrag = false` on the dock's slots: the slot
  handed Godot drag data on any motion past 8px and hung a ghost of
  the icon under the finger for the length of every scroll.
- The bar: `M59Skin.SlimScroller` - 28 wide to the thumb
  (`SlimBarW`; the brief's floor was 24), 16 visible, no BarInset
  because nothing sits beside it, and the grabber never shorter than
  44: Godot has no grabber minimum of its own but respects the
  grabber stylebox's minimum size, which for a flat box is its
  content margins - 22 top and bottom. Scroller's own comment
  promises this and does not do it.
- A LESSON that reached every panel: `TouchScroll` armed on a press
  on the bar too, marked the motion handled, and the grabber never
  saw its own drag - what the finger got was the content drag, which
  runs the other way (pull the grabber down, list scrolls up). Now a
  press inside `GetVScrollBar().GetGlobalRect()` is left to the bar
  (`TouchScroll._Input`). Found by dragging the dock's grabber in the
  harness and seeing the first rows still there.
- Harness: `M59_PACK=n` pads the fixture's pack to n (same pattern as
  `M59_BUFFS`; `M59_BIGBAG=1` is now `M59_PACK=100`). Screen
  coordinates are NOT UI coordinates - the safe-area layer sits at
  ~0.95 scale, so the bar at UI x 506..534 is on screen at ~529..556;
  the first grabber drag at 520 landed on the eighth slot.

Played (harness, 1920x1080): 6 items -> box (16,543,490,118), two
rows, no bar. `M59_PACK=40` -> box 518 wide, bar with grabber at the
top, rows 1-2; `@drag:200x640>200x480@30` -> rows 3-4 shown, grabber
down, no strip (the lift after a scroll does not pick); `@tap` on a
slot -> ring + strip under the box + target card; same slot ->
strip gone. `@drag:542x565>542x650@30` on the grabber -> last rows,
grabber at the bottom. Arrange: card shows "Across 8 / Down 4" after
two "+", handle 242 tall over the box, chip "4 down", chat clear;
Done, relaunch on the same user data -> `dock=0,0,1,1,0,0,4`, box
242.

See also: InventoryDock.cs | M59Hud.cs | HudEditor.cs | the HUD editor -> godot-ui.md | the bag -> godot-ui.md "A model object held across a rebuild goes stale"

## The hotkey box: the hotbar on the glass, hidden until asked for
Tags: design, architecture | An OPTIONAL HUD piece ("hotkeys" / "Hotkey box") drawn exactly as the inventory dock is - same slot, box, slim bar, Across/Down - showing every hotbar seat by Num; a tap goes through the cluster's own Perform, Edit opens the HotKeys panel, and the piece starts hidden (M59Hud.Piece.DefaultHidden)

Ashton (2026-10-03): "add a new optional box players can unhide in the
ui customizer. A hotkey box that looks just like the inventory box and
they can change the rows and colomns just like the inventory. And let
them edit it with the hotkey customizer." `HotkeyDock.cs`.

- HIDDEN BY DEFAULT, which the model had no word for: `Hidden` was
  only ever the player's choice and `Moved` wrote it as a change. Now
  `M59Hud.Register(id, name, node, hidden: true)` sets
  `Piece.DefaultHidden` once, before the file is read, and that is
  what "default" means for the piece: `Moved` compares Hidden against
  it (an untouched optional piece writes no line), Reset and a layout
  switch go back to it, and unhiding writes `hotkeys=0,0,1,1,0` -
  hidden=0 is the player's choice, read as such by ApplySaved. The
  editor needed nothing: Dress draws a hidden piece faint while
  editing, a tap picks it, the Hide button reads "Show", and the
  Across/Down steppers already act on whichever picked piece declares
  a range (`HudEditor.HasColumns/HasRows`), so the card is the dock's.
- THE SEATS are the cluster's model uncut: every config in
  `Data.ActionButtons` by Num (`ActionButtons.Stable`), Attack among
  them as one more seat, an Unset config as the empty square - the
  arc is positional and a hole the HotKeys panel made is a hole here.
  Grid padded to the box as the dock's is. Slots are named
  `hotdock{Num}` / `hotdockEmpty{i}`; the strip is `hotdockStrip`
  with one button `hotdockEdit`.
- ONE PRESS PATH. `ActionButtons.Perform(cfg, repeat)` is new and is
  the cluster's private `Send` made reachable - Run (HotbarAct) around
  `Activate()`, or the view's SendReqGo for the Door seat. The view
  hands it to the box (`_hotdock.Perform = _hotbar.Perform`), so what
  Rest sends is decided once. A held Attack repeats every frame past
  250 ms as the cluster's does (`ActionButtons.IsAttack`); the other
  repeating kinds are tap-only in the box, because the cluster's rule
  for an item reads its own hold state and that was not worth sharing.
- SYNC: a signature of Num/type/name plus the art's resolution state
  per seat (filename, colour, effect, frame), the ListChanged
  subscription the cluster uses (field-only rebinds), Across/Down/
  icon pixels, the HUD stamp every frame, and the 500 ms retry on a
  missed compose. `Covered` is set from `GameView.Pump` beside the
  dock's, and gates the scroll box's input and the hold.
- THE PIECE IS BOX + STRIP, unlike the dock, whose strip comes and
  goes with a selection: Edit is always there, so the handle and the
  overlap test cover it. Natural place: a gutter right of the
  inventory dock's NATURAL rect at the same top - (522,543) at
  1920x1080 - clear of the target card (1336+) and above the chat.

Played (harness, 1920x1080): fresh user data -> `hotdockHost
Visible=False`, nothing on the glass; Menu > Settings > Arrange, tap
the faint handle, Show, Across -2, Down +1, Done -> `hotkeys=
0,0,1,1,0,6,3`, box (522,543,366,180), seats Rest Attack Loot Activate
Inspect Buy / Trade Wave; relaunch -> same; `@name:hotdock0` ->
`<- UserCommand Rest` on the fixture and the RESTING banner;
`Target Next,@hold:hotdock1` -> ten `ReqAttack`; Edit -> the HotKeys
panel; `hk1` Clear, `hk0` ▶, Close -> the box reads [empty, Attack,
Rest, ...]; five Add pages -> 30 seats, bar in its own 28-point gutter
beside the slots, a drag on the grid scrolls it; drag the handle in
the editor, Done, relaunch -> `hotkeys=362.11,-312.63,1,1,0,6,3` and
the box at (884,230).

See also: HotkeyDock.cs | ActionButtons.Perform | M59Hud.Piece.DefaultHidden | the HotKeys panel above | the inventory dock above

## The chat box: width and lines in the arrange screen
Tags: design | The "chat" piece borrows the store's Columns/Rows as WIDTH (steps of 40 points) and LINES; the piece names its own axes on the card (Piece.ColumnsLabel/RowsLabel, ColumnsUnit/RowsUnit); zero means today's box exactly

Ashton: "make the chat box height and width adjustable in the
customizer." Scale already grew the whole block together; he wanted
the two axes apart. `ChatOverlay.cs` (the WHY block over `WidthStep`).

- Same mechanism as the dock, no new fields: the chat sets
  `MinColumns/MaxColumns/DefaultColumns` and `MinRows/MaxRows/
  DefaultRows` on its piece and reads `ColumnsNow`/`RowsNow`; the
  saved line is still `x,y,scale,alpha,hidden[,columns[,rows]]`
  (`chat=0,0,1,1,0,29,12` after Width +4, Lines +4 at 1920x1080).
- THE UNITS. Width = count x 40 points: a pixel step needs a thousand
  presses, a tenth of a screen cannot land on "a bit wider". Min 8
  (320, the narrowest a line still reads), max = whole steps inside
  the margins and never above 48 (47 at 1920). Height = LINES, because
  a text log is made of lines and the pixel height is pitch x lines x
  scale, so the box holds whole lines; 3..20, default the designer's
  `Lines` (8). Width does NOT follow Scale (it never did); the line
  pitch does.
- ZERO IS TODAY. With no sixth field `BlockWidth` keeps the old
  half-screen rule and `LinesNow` is `Lines`, so a layout from before
  is pixel-identical (diffed the chat region before/after: no
  difference). `DefaultColumns` is today's width to the nearest step
  (25 at 1920), set in Layout because it depends on the glass, so the
  first "+" is one step wider, not a jump. `BlockWidth`/`BlockHeight`
  follow the player's counts, so the hotbar's LeftReserve and the
  reserves GameView reads each frame move with the box.
- THE CAPTIONS ARE THE PIECE'S. "Across"/"Down" are a grid's words;
  the card reads `Piece.ColumnsLabel`/`RowsLabel` ("Width"/"Lines" for
  the chat, defaults "Across"/"Down" so the dock is unchanged) and
  shows the count times `ColumnsUnit`/`RowsUnit` ("Width 1000", "Lines
  8"; dock unit 1). The chip on the handle says "1160 width  12
  lines". The saved number is always the COUNT; the unit is display.
- Reflow: the log is a RichTextLabel given an explicit Size, so
  autowrap already measures at the new width; `HudStamp` carries the
  two counts so Sync re-lays after a step, and Layout sets `_dirty`
  when the line count moved so the strip is refilled with the last N
  (the early-out would otherwise keep the old text). Chat/Log sit on
  the block's bottom row as before, so they travel with it.

Played (harness, 1920x1080, M59_CHATFLOOD=1): card on the chat reads
Width 1000 / Lines 8; four "+" each -> 1160 / 12, handle 1106x312
over the box, 12 messages, Chat/Log under it, world around it; Done,
relaunch on the same user data -> `chat=0,0,1,1,0,29,12`, same box;
Width "-" to the floor -> `chat=0,0,1,1,0,8`, 320 wide, every
message wrapped inside the box, newest at the bottom; a hand-written
five-field `chat=40,-60,1.2,0.8,0` -> moved, 1.2x, 80%, 8 lines at
the old width.

Seen and left: at 1.00x the first of the N lines is clipped a few
pixels at the top (the pitch `FontSize + 6` is a hair under the font's
real line height with ScrollFollowing on); it was so before this and
is not at 1.2x. A pitch read from the font would fix it.

See also: ChatOverlay.cs | M59Hud.cs | HudEditor.cs | the dock above

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

## One scene unit is sixteen room units
Tags: gotchas, lessons | The reference's heights and thresholds are in scene units; SCALE is 0.0625, so every `16.0f` in RemoteNode.cpp is 256 room units - the unit mix-up has now caused two bugs

`Util::GetSceneNodeHeight` answers in scene units: it measures the
billboard, whose size is `RenderInfo.WorldSize.Y`
(`Util.h:847-867` via `RemoteNode2D.cpp:88-93`). The room is built at
`SCALE = 0.0625` (`ControllerRoom.h:73`), so one scene unit is 16 room
units. The name label's dead band (`abs(diff) > 16.0f`,
`RemoteNode.cpp:494-500`) is therefore 256 here, and its `+ 1.0f` lift
(`:491`) is 16. `NameTags.Deadband` and `QuestMarkers.Deadband` are 256
(`NameTags.cs:82`, `QuestMarkers.cs:69`) and `NameTags.Lift` is 16
(`:54`); the label views had it wrong first, and a name bobbed on every
animation frame.

LIVE QUESTION: the camera's eye deadband had the same mistake. At HEAD
(fa28d39) `WorldSync.cs:487` compares against `16f` where the reference's
16 scene units are 256 - sixteen times tighter, so the view bobbed on an
animation frame. It is being corrected in the working tree (an
`EyeDeadband = 256f`, compared on the eye, 0.93 of the height, as
`RemoteNode.cpp:411` vs `:420` do); check that landed before trusting
this paragraph, and check any new `16` taken from the reference.

See also: NameTags.cs | WorldSync.cs

## Names are drawn after the frame, and are depth-tested
Tags: gotchas, lessons | `Renderer.Project` projects against the LAST rendered camera and the depth buffers Render fills, so the label Syncs run after `RenderFrame` or they lag by a frame

`Project` has only `_lastCam*`, which `Render` alone writes. The two
label Syncs used to run with `SyncSprites`, before `RenderFrame`, so a
name was placed for camera(N-1) with position(N) and slid off its owner
for as long as you kept turning, by an error that grew with turn rate
(the commit measured 5.8 to 44.3px at 0.01 to 0.10 rad per frame, 1.5 to
10.2px after). They run after it now
(`GameView.cs:2968-3000`), the only order in which the occlusion tests
mean anything: `Project` tests the per-column wall depth
(`Renderer.cs:1364`) and, since the sprite pass keeps its depth buffer,
the nearer BODY too (`:1375-1383`, valid only on frames that painted
something). In the reference a label is an alpha-rejection pass with the
depth test on. Do not add a label view that Syncs earlier.

A camera that moves is not photographable by the stock harness steps -
see harness.md.

See also: NameTags.cs | the harness -> harness.md

## Composed pictures carry a mip chain, built eagerly
Tags: gotchas, lessons | M59Compose.Raster returned a Tex with no mips, Tex.Level() then answers level 0, and every room object was point-sampled at full resolution

`WorldSync.Composed` defaults true, so the composed path is the one every
room object takes, and the single-frame path's mips never applied to it.
`M59Compose.Build` calls `Mip(t)` (`M59Compose.cs:44,89-92`: `RebuildMips`
then `KeyAlpha(64)`, base_material's own rejection threshold). It is
EAGER on purpose: lazy building is what made the threads oracle disagree.
Anything that writes level 0 afterwards must re-mip: the target outline
does, in `ComposeCache.Get` (`M59Compose.cs:416`), or a distant target
silently loses the red edge the ruling calls for.

The check that showed it: the share of painted pixels that change colour
when a sprite slides half a screen pixel, 10.9% or worse before and 0.0%
after for a distant Knight (5d4c988's measurement, not re-run).

See also: Renderer.cs | M59Geo.cs

## A frame the art does not have is not drawn
Tags: gotchas | SpriteCache.Get clamped a bad animation group to frame 0 and drew a creature frozen in the wrong pose; the library draws nothing

`GetFrameIndex` answers -1 for a group the art lacks and for a facing the
group has no frame for (`BgfFile.cs:490-514`); `Get` returns null
(`M59Geo.cs:466-471`) and its callers already cope. The clamp fired on
every lookup for a group the art lacks, and on 242 of 16976 for a group
it HAS (a facing with no frame), those in four BGFs: `gshnecbk`, `gshnecov`, `maulov` (worn parts, which reach the
screen through the composed path) and `hist_king1`, whose index runs
past its frame list - counts as measured in the code comment, not
re-run. A null from `Get` for one of those is the library's answer.

See also: M59Geo.cs

## A command that fails is never speech, and Parse is not pure
Tags: gotchas, lessons | Every malformed chat command parses to null; reading null as "this is speech" sent `tell Zorak <private words>` to the whole room

`ChatCommand.Parse` pushes a ServerString into `Data.ChatMessages` on
each failure path (listed at `GameView.cs:96-106`), so the line is parsed
EXACTLY ONCE - classifying with `IsCommand` and then parsing again
printed every error twice. A null parse sends nothing, as the reference
does (`UIChat.cpp:274-281`). The Who list's Tell composes the name
QUOTED, because the unquoted branch takes the shortest prefix that
matches one player and treats the rest as the body
(`ChatCommand.cs:416-440`), so a multi-word name lost half of itself.

See also: GameView.cs | wire-format.md

## Sound: two more ways it was silent
Tags: gotchas, lessons | The string table and the files disagree about case, and the library poisons its path cache with the bad spelling; and a batch of messages inverts the reference's per-message order

- Names differ in case from the files on disk (`ambcave.ogg` for
  `AMBCave.ogg`; `M59Sound.cs:712-730` has the count, 213 of 495), the
  load is case-sensitive on Android and Linux, and
  `ResourceManager.GetWavFile` writes the failed path back into a
  case-insensitive dictionary (`ResourceManager.cs:403`) so one bad
  request fails the right one too. NTFS hides it in the reference.
  Resolved through a lowercased index of the folder
  (`M59Sound.cs:751-762`); `Tools/Meridian59.Net8FakeServer/sound-guard.sh` is the pass/fail check.
- `_client.Update()` drains the whole socket and fires Sound for every
  message in the batch; the reference dispatches per message and clears
  its list on reaching Player (`ControllerSound.cpp:229-256,341-357`), so
  a PlayWave AFTER Player survives. Stopping afterwards in `SyncRoom`
  killed every room's loop in the frame it started (`GameView.cs:1232-1250`).
  The fixture only catches it with `M59_LOOPFIRST` -> fake-server.md.

See also: M59Sound.cs | the pack -> "Sound shipped, and did not arrive"

## The first frame in a new room pays for the whole room

The texture cache is lazy, which is right for a cache and wrong for a
room change. The first frame after walking through a door is the one
frame that touches every wall, floor and ceiling the player can see,
and it decoded each of them - palette, pixels, mip chain - on the
render thread, which is the hitch.

`Renderer.Warm` decodes everything the room file names, and
`WorldSync.SyncRoom` starts it on a background task the moment the
renderer is built. Nothing about correctness moves: the cache is
keyed and locked exactly as before, and a frame that beats the
warm-up to a texture still builds that one itself, waiting on one
decode rather than on the room.

Measured, `RenderCheck -- cold`, 24 rooms at 1280x432:

    cold first frame   16.5 ms
    warm first frame   11.4 ms
    settled frame      10.9 ms

So a warmed first frame costs about what a settled one does, which
is the point. The worst room in the set went 51.6 ms to 5.2 ms.

Two things that would have made the measurement a lie, both avoided
in the tool: ResourceManager caches the BGFs behind the TexCache, so
the warm run needs its own manager or it is credited with work the
cold run did; and every other oracle renders a room many times, so
all of them pay the decode once, in a frame none of them time -
which is why this number had never been looked at.

The previous room's warm-up is cancelled rather than waited for: its
textures belong to a cache that went with its renderer, so on a run
of doorways they would stack up decoding bytes nobody will read.

## Sprites are depth-tested against floors and ceilings, per pixel
Tags: gotchas, lessons | Walls were closed by depth and the window; flats were in neither, so a creature beyond a raised platform had its legs painted over the platform's top - `Renderer._flatDepth` is the fix, and the `flatdepth` oracle covers it

The player's screenshots: an NPC on the lower floor behind a raised
wooden platform with his legs over the planks, and an orb at the foot
of a staircase over the treads. The sprite pass tested `_depth[sx]`
(the wall that closed the column) and `Window` (the upper and lower
parts the walk passed - the lintel fix). `Narrow` is only ever called
from those two parts (`Renderer.cs` RenderBand, the `fc < nc` and
`ff > nf` branches), so a floor that drops AWAY (`ff < nf`) narrowed
`yBot` and recorded nothing, and the platform top's own pixels were in
no depth structure at all. A hill crest inside one sloped sector was
the same hole.

The reference has none of this: every room fragment writes depth,
water included (`general.material:257-285`, `:414-445`), and a
billboard is depth-tested per pixel. So the honest target is what a
depth buffer does, and the flats now have one: `FillFlat` stores the
`straight` it already solves per row into `_flatDepth[y*W+sx]` (one
store, no divide), reset to MaxValue on the frames `_spriteDepth` is
cleared and trusted only then (`_spriteValid`). `DrawSprites`,
`Project` and `PickAll` all test it - the picker has to, or the pick
oracle fails (painted and pickable must be the same set). The sprite
test is strict with no epsilon: a creature's lowest drawn row is half a
pixel above its feet (TexelY cuts at `y + 0.5`, FillFlat solves at
`y - horizon`), so the floor pixel there is beyond the feet. Move
either convention and the feet row ties, and a tie is "behind" - the
`flatdepth` check (creatures on the level floor of the eye's own leaf
must lose nothing: 0 of 3.1M px) is what catches that.

Two things the oracle taught while being written. A rat whose centre
is in the leaf but whose flank hangs over the merchant's counter
loses the flank, correctly - the check had to put the WHOLE billboard
in the leaf. And `M59Geo.FloorXY(sec)` is the sector's base height:
planting sprites there, as the lintel and pick checks do, stands a rat
on bergleader's ramp 930 units under the surface, so the first count
was mostly of buried sprites. The game's heights come from
`GetHeightAt`, which follows the slope; the flatdepth check uses
`FloorXY(sec, x, y)`. Water: an object wading is sunk by the sector
depth and the surface now hides it from the waist down, which is what
the reference's depth-writing water does.

Cost, measured with the oracle's timing (1280x576, 12 sprites, best of
8 blocks on the 2-core box): 6.9 ms before (store and fill stubbed
out) to 6.4-7.3 ms after, within the run-to-run noise; the fill alone
is 0.18 ms there and 0.54 ms at 2424x1080, the same order as the
`_spriteDepth` clear already paid.

Photographed: barsmith, fixture spawn `M59_SPAWN=252,172,0`, the rats
on the 3488 floor beyond the 4000 plank platform - before, both rats
over the planks at the bottom of the frame; after, the planks whole and
the rats cut at the platform's far edge.

See also: Renderer.cs | the oracle -> ../../Tools/Meridian59.Net8RenderCheck/README.md

## Wading is the library's height and the floor's depth, and the fixture has almost no water
Tags: gotchas, lessons | "Monsters float on water" could not be reproduced where the water has its art; where it has none there is no floor, no depth, and the sprite is drawn through the hole - in the reference too

The report: creatures and players stand ON water instead of wading.
Traced end to end, nothing in the client stands them up. The library
takes the sector depth off every non-hanging object's height
(`RoomObject.cs:1081-1082`, `:1184-1185` -> `RooSector.cs:813-841`,
FINENESS/5, 2/5, 3/5 from `RooFile.cs:120-127`), the reference draws
the node at that `Position3D` unchanged (`RemoteNode.cpp:510-515`) with
the camera on it (`:406-425`), and here `WorldSync.SyncSprites` puts
`Position3D.Y` straight into `BaseZ`, `Camera` puts it under the eye
and `NameTags` under the label. A live trace in bergleader_hall's pool
(`M59_SPAWN=700,920,0`, Depth1 on grd01802) had the far rat lowered to
4996 under a 5200 surface, painted rows 249-367 against a waterline
solved at 368, the translucent one cut at 418 against 418. The `wade`
oracle now proves it in every room with drawn water: 0 px under the
waterline, 0 lost above it, 37779 of 171353 hidden.

The trap that made it LOOK broken for a whole session: `/tmp/res` is
missing grd08895 and the 8911 family, most of the game's water, so in
jixa's fountain, barlsew's channel and barsmith's trough the floor
texture resolves to null, `FillFlat` paints sky and stores NO depth
(`Renderer.cs`, the `t == null` early-out), and the sunk rat is drawn
through the hole to its feet - exactly "floating". The reference
builds no floor for a sector whose BGF is missing either
(`ControllerRoom.cpp:789-790`), so that is fidelity, not a bug; the
shipped pack is the full 4,700 files. Only 124 of the fixture's 1689
depth sectors have their floor art; the oracle counts the rest as
skipped and says so. Test wading in bergleader_hall, berg_devroom or
guildh16, not in the sewers.

What the reference leaves un-reheighted, and so do we: a SectorChange
that alters a sector's depth while objects stand in it
(`RooFile.cs:2266-2273` -> `RooSector.ApplyChange`) moves nobody until
they next move, since `UpdatePosition` gates on `IsMoving`; only a
sector MOVE re-heights its standers (`DataController.cs:1700-1712`).
Server-104 sends its depth changes straight after Player and before
the contents (`user.kod:3395-3399`, the fixture's `SendWade` order),
so objects are created at the changed depth; a depth that changes
mid-room would stand them wrong until they step, in both clients.

See also: the oracle -> ../../Tools/Meridian59.Net8RenderCheck/README.md | WorldSync.cs
