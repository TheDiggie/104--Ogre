# The fake server

`Tools/Meridian59.Net8FakeServer` stands in for Server 104 so the client
can be played without a live game. It is also the single largest source
of false bug reports in this port.

## Check the fixture before blaming the client
Tags: lessons, process | Spell art, quest art, the EQUIPPED flag, picking things up, buying and the wizard's face parts each looked like missing client code and were missing test data

The rule that came out of it: when a feature looks unimplemented, prove
the server is sending the data before reading a line of client code.

See also: the client -> ../README.md | harness.md

## It dies, and a dead server looks like a broken client
Tags: gotchas | A run against a dead fake server returns a flat grey frame, because the client never connected - pgrep it and read head -3 /tmp/fake.log first

See also: harness.md

## Parsing notes
Tags: gotchas | ReqBuyItemsMessage parses a whole TCP message and throws on a body; hand-parse PI, seller, ushort count, ObjectIDs. An offer names the partner first, a counter-offer does not

`ReqBuyItemsMessage(body)` throws `WrongLEN - input:256 expected:8061`.
Reading a counter-offer as an offer gives a nonsense count and prints
nothing, which reads as "trade is broken".

See also: wire-format.md

## Attacking, and the off-by-one that hid it
Tags: gotchas, lessons | ReqAttack's body is [PI=103][01][ObjectID], so the id starts at offset TWO - read at one, as ReqLook's does, 2001 comes back as 0x7D101

The fixture ignored ReqAttack entirely, so nothing downstream of a kill -
the target clearing, the red outline going away, the object leaving the
room - had ever been exercised. Teaching it to answer took three hits and
a `RemoveMessage`.

The first attempt read the id at offset one, which is where ReqLook's
sits. It came back as 512257 rather than 2001: the id shifted up a byte
with that `01` pulled in underneath it. The removal then named an object
that did not exist, the client correctly ignored it, and the rat looked
unkillable - an hour spent reading client code that was right the whole
time. Dump the bytes before believing a parse.

See also: the client -> ../README.md | wire format -> wire-format.md

## The server's half of a trade
Tags: gotchas, process | An offer used to go out and nothing come back, so IsItemsYouSet stayed false and the window sat on "Offer" forever

The fixture now answers with `OfferedMessage` for an offer and
`CounterOfferedMessage` for a counter-offer. With it, the window fills in
"You offer" from the server rather than only locally, and the buttons
collapse to Cancel alone.

Accept stays hidden throughout, and that is correct: the reference gates
it on `IsItemsYouSet && IsItemsPartnerSet && !IsBackgroundOffer`
(`UITrade.cpp:94-97`), and a trade the other party opened is a background
offer. You counter; they accept.

See also: the trade panel -> mobile-client.md

## There are two rooms now, and Go moves between them
Tags: process | ReqGo re-enters: second room, second Go back again - one button exercises the whole room-change path without a door to stand on

A second room is the smallest fixture that proves a room CHANGE rather
than a room. The second one is deliberately barer - the avatar and one
rat - so an object list that failed to clear would show.

Its spawn point is not hard-coded. Coordinates that are fine in barinn
land in rock in another .roo, and a client standing in rock reads as a
client bug, so the fixture reads the room, takes the roomiest leaf of
its BSP tree and converts the centre: kod = room/16 + 64, the same
conversion `BaseClient.SendReqMoveMessage` does in reverse. Run against
barinn it returns 752,672 - exactly the coordinate this fixture has
used by hand since the beginning, which is the check that the
conversion is right.

The second room is `barlmarket.roo`, which is lit: `a1.roo` worked but
is near-black where its biggest leaf is, so every shot of the room
change was a black rectangle that proved nothing to the eye.

See also: the client -> mobile-client.md | harness.md

## Once per session is not once per room any more
Tags: gotchas, lessons | The avatar's buff icons doubled after a Go out and back - the fixture was re-sending session things from EnterRoom, which used to run exactly once

Making Go change rooms made `EnterRoom` run again, and its tail carried
everything a session needs, not everything a room needs: the trade
offer, the chat flood, the stat-change timer and the avatar's two
enchantments all went out a second time. `AvatarBuffs.Add` appends and
nothing clears it, so the icon strip showed the same buff twice.

The client is right. `DataController` clears `RoomBuffs` on both
RoomContents and Player (`:2225`, `:2346`) with a comment saying the
server re-sends those - and says nothing of the sort about avatar
buffs, because a real server does not re-send them for a door.

So the tail is now split: sounds, music and the room's own two
enchantments per room, everything else behind a `sessionExtrasSent`
guard. Found by stacking the same 200x30 crop from before and after and
counting icons, not by reading code.

See also: harness.md | the client -> mobile-client.md

## A changed string in the .rsb does not reach the client
Tags: gotchas, lessons | EnsureStrings skipped the rewrite when every id was present, so an id whose TEXT changed kept the old value - the server said barlmarket and the client loaded a1, both logs looking right

The check compares text as well as ids now. The failure is nasty
because nothing errors: the fixture prints the room it meant to send,
the client prints the room it actually got, and the two lines are
twenty lines apart in different logs.

Delete `rsc0000.rsb` from the resource folder if the dictionary ever
looks stale anyway; it is rewritten on the next start.

See also: harness.md

## M59_PARALYZE and M59_WAIT
Tags: process | The two notifier states nothing in the client can cause - both are the server holding you still

`M59_PARALYZE=1` sends an `EffectParalyze` six messages in and an
`EffectRelease` at thirty; `M59_WAIT=1` does the same with Wait and
Unwait. Resting needs no switch: the Rest button sets `IsResting`
client-side before the command even goes out
(`BaseClient.cs:1377`).

See also: the notifier -> mobile-client.md

## Unhandled messages are logged by name
Tags: gotchas | "game-mode 100, ignored" was read as a ping once, and an evening went into believing the client could not walk

The default case prints the enum's name where there is one: Ping (3),
ReqMove (100), SendEnchantments (53). ReqCast and ReqPerform have cases
of their own for the same reason - whether the message reached the wire
is the first question every spell bug asks.

See also: harness.md

## M59_SHOOT
Tags: process | An arrow from the far rat to you every twelve messages - the only way to see the projectile path, since nothing the client sends causes one

Speed matters: the library's `Projectile` constructor used to drop the
Speed parameter, leaving zero, which is Teleport - the projectile
arrives in one tick and exists for a single frame. With speed 8 the
flight is about a dozen frames, which is long enough to photograph.

See also: the client -> mobile-client.md

## The test surface: switches for what the client could not reach
Tags: process | Door and lift geometry, a server move refusal, a room-change gap, a missing room, stat icons, skill look, room buffs, a shrinking stack - each an off-by-default env var; unset, the fixture behaves exactly as before

Two audits found whole client areas unreachable because the fixture
could not produce the message. Every switch below is read once at start
(M59_* env vars, like the rest), is off unless set, and was checked
against the original build: scenes entry, Go/Go, Book/Bag/Me and a walk
drag give identical server logs and identical world pixels. Every
message is built from the Meridian59 library's own class, and each
switch carries a comment in Program.cs naming the real-server behaviour
it stands in for and the kod/class line.

Timing is counted in client messages, not milliseconds (as M59_SHOOT
is). Geometry starts M59_GEOM_AFTER messages after a room entry
(default 6) and repeats every M59_GEOM_PERIOD (default 24, open then
close on alternate beats). Pings outpace frames under load, so a screenshot run
needs a long list of dummy --press steps (names that match nothing) or
the message fires before frame 1 and every shot looks unchanged.

- Doors and lifts. M59_SECTOR=<id> moves a sector (SectorMoveMessage,
  user.kod SectorSendUser): M59_SECTOR_PLANE floor|ceiling,
  M59_SECTOR_TO height (400), M59_SECTOR_BACK height to return to,
  M59_SECTOR_SPEED (16). M59_SECTOR_CHANGE=<id> sends SectorChange
  (depth M59_SECTOR_DEPTH, scroll M59_SECTOR_SCROLL, 4 = leave;
  ..._BACK values restore; user.kod SectorChangeSendUser).
  M59_WALL=<sidedef> sends WallAnimate (M59_WALL_ANIM, _PERIOD, _GROUPS,
  _ACTION; proto.h ANIMATE_*, user.kod WallSendUser).
  M59_WALLTEX=<sidedef> sends ChangeTexture (M59_WALLTEX_PART, _TO 1018,
  _BACK; proto.h CTF_*, user.kod TextureSendUser).
  Verified recipes: barinn.roo with M59_SPAWN=800,1060,2 M59_WALLTEX=32
  M59_WALLTEX_TO=102 M59_WALLTEX_BACK=11065 M59_WALL=32 M59_WALL_GROUPS=1,3
  M59_WALL_PERIOD=300 M59_GEOM_AFTER=10 M59_GEOM_PERIOD=40 - the door wall
  turns to texture 102, animates, and is restored. Ceiling: barinn,
  M59_SECTOR=0 M59_SECTOR_PLANE=ceiling M59_SECTOR_TO=150 M59_SECTOR_BACK=272.
  Lift: barrent.roo, M59_SECTOR=8, floor. A target of 0 matches every
  sector with no ServerID (RooFile.cs:2250-2284 matches by ServerID) -
  that is the client's rule, not a bug in the switch.
- M59_YANK=N refuses every Nth ReqMove: MoveMessage to the last good
  square plus TurnMessage, as user.kod UserMove does through
  UtilGoNearSquare (util.kod UtilGoToSquare). M59_YANK_TO=x,y,
  M59_YANK_SPEED. Seen: the drag walks the avatar away without it and
  back to the start with it.
- M59_ROOMGAP_MS=N holds RoomContents back N ms after a Go (Player is
  sent at once), the window the real server leaves while it builds the
  room. Applies to a Go, not the first entry. Seen: one frame with
  "0 objects" and no avatar, then the contents.
- M59_BADROOM=1 makes room 2 a file the client lacks (zzmissing.roo,
  RoomID 3). The client logs "has no .roo - nothing loaded" and shows
  the failed-entry report.
- M59_ROOM2=<file>, M59_SPAWN=x,y[,angle], M59_SPAWN2=x,y[,angle]: room
  2's file, and where each room puts the avatar. Room-1 objects are now
  placed relative to the spawn; unset, the spawn is barinn's 752,672 as
  before (barlmarket as room 1 was run and works).
- M59_STATICONS=1 gives stat rows a ResourceIconID (user.kod 9409-9447);
  spells get coin/book art and skills axe/coin, deliberately different from
  the object's own art so the row icon is what is seen.
  M59_LOOKSKILL=1 answers ReqLook on skill 5101/5102 with a
  LookSkillMessage (skill.kod ShowDesc): school, level, description.
- M59_ROOM2BUFFS=1 sends a RoomBuff in room 2 (room 1 has two).
- M59_STACK=shrink|replace, M59_STACK_AFTER (12 messages), M59_STACK_TO (5)
  changes the 25-coin stack mid-session. shrink is a ChangeMessage with a
  full ObjectUpdate (numbitem.kod SubtractNumber -> NewNumber ->
  SomethingChanged); replace is InventoryRemove then InventoryAdd.
  Seen: the Bag shows 25, then 5, with no ReqInventory in between.
- M59_AMBIENT=n sets the rooms' ambient light. See the gaps.

The "client ignores a hand-built ChangeMessage" report: not a client
ignore. A well-formed one is applied (DataController.HandleChange stores
NextUpdate and applies it on Tick). The ObjectUpdate constructor leaves
LightingInfo and Animation null, so a message built without them is
malformed; give it LightingInfo, AnimationNone and empty overlay lists.
I did not reproduce the auditor's exact bytes.

Gotcha: each server rewrites rsc0000.rsb, which carries the room's file
name, so two servers sharing one resource dir clobber each other (every
run loaded the same room). Give each run a private dir of symlinks with
its own rsb.

Not closed:
- CTF_RESET: RooFile.HandleChangeTexture ignores it, so the switch
  restores by sending the original texture id instead.
- SectorChange has no visible effect to show: the client does not render
  sector scroll, and depth acts on the avatar only when it moves. The
  wire bytes were checked; the client effect was not seen.
- The library's MoveMessage carries an angle, Server-104's BP_MOVE does
  not; the yank sends what the library class writes.
- Kod has no separate LookSkill packet; the switch uses the library's
  LookSkillMessage, which has no kod twin to cite.
- The door-state replay on room entry (room.kod:2196-2204, speed 0) is
  not implemented; geometry starts from the geometry timer.
- M59_AMBIENT is sent but no visible brightening was seen on barrent at
  255; treat it as unverified.

See also: the client -> mobile-client.md | harness.md | wire-format.md

## The sound surface: the whole of it was three messages in one room
Tags: process, lessons | A one-shot, one track and one loop in room 1 hid seven audio bugs; nine off-by-default switches (M59_LOOPFIRST, ROOM2SND, MUSIC, WADE, SNDNAME, SNDSTOP, LOOPS2, SNDOWNER, SNDGHOST) now cover them, measured on the Master bus

The audit's gaps were all real, checked in Program.cs before building:
room 2 (`EnterSecondRoom`) sent no PlayWave and no PlayMusic; there was
one music id; both rooms sent `WadingSoundFileRID: 0`; the loop and the
one-shot were the same id (`RID_RATSOUND`), so a stop could not tell them
apart; every table string matched its file case-exactly and no file was
ever absent; every loop had id 0; one loop, the music never re-sent, no
id for an absent object. One more turned up reading the server: the
fixture sent the room's loop AFTER RoomContents, while `ToCliPlayer` sends
BP_PLAYER and then `SendLoopingSounds` at once (`user.kod:3370-3395`), so
"Player and the loop in one drained batch" - the shape of the
ambient-killed-on-entry bug - was not what the fixture produced either.
The real wading default is `splash.ogg` in EVERY room (`room.kod:81,192`),
so the fixture's 0 was the odd one out.

Files: the test dump holds two oggs (both 2 s of 440 Hz at -21 dB), so
`Tools/Meridian59.Net8FakeServer/make-sounds.sh <dir>` writes seven
synthetic ones (FXLoop1/2/3, FXShot 10 s, FXRoom2, FXMusic2, FXSplash),
each at its own level so the bus says which is sounding. Run each server
on its own port with its own dir of symlinks plus those files - the
server rewrites `rsc0000.rsb` there. Timed events count client messages
from the room entry (M59_SND_AFTER, default 8 - which also moves the old
room-1 loop stop, so set it the same in a control). Pings are ~1 a second
after the start-up burst, so 17 is about 3 s, 25 about 10 s.

- M59_LOOPFIRST=1: room 1's loop goes out right after Player, not after
  the contents. Room 2's own loop always does (it is new).
- M59_ROOM2SND=1: room 2 sends its own loop (FXRoom2, at the spawn's grid
  square, `room.kod:686`) after Player and its own track (FXMusic2) after
  the contents (`room.kod:2129`). A Go is now a change of track.
- M59_MUSIC=swap|repeat|zero: the other track / the same one again /
  resource 0, M59_SND_AFTER messages in (`feyforst.kod:264`,
  `room.kod:2129`, `user.kod:4530-4545`; `ControllerSound.cpp:485,492-519`).
- M59_WADE=1..3 with M59_WADE_ROOM (1, 2): that room's Player carries
  FXSplash and a SectorChange puts the spawn's sector at that depth
  (`room.kod:2272-2285`, `user.kod:3399,4757`). The wading name is sent
  as .ogg, as Server-104 names everything: RoomInfo does not do the
  .wav-to-.ogg swap PlaySound does (`RoomInfo.cs:849`). Needs a walk.
- M59_SNDNAME=miscase|missing: room 1's three sounds under names in the
  wrong case (rat_awr.wav, RAT_AWR.WAV, ambcave.wav); or sounds and a track
  whose files do not exist, plus a stop for one that never started.
- M59_SNDSTOP=none|loop|oneshot: splits the shared file (loop FXLoop1,
  one-shot FXShot from rat 2001) and sends the stop by name, no object
  (`user.kod:4518`, `room.kod:742`). M59_LOOPS2=1 adds a second loop
  (FXLoop2; `room.kod:662-690`) that a stop of the first must leave alone.
- M59_SNDOWNER=1: a loop owned by rat 2003, then the rat is removed with
  no StopWave. M59_SNDGHOST=1: a loop with source 9999, not in the room,
  then a stop naming 9999. They share FXLoop3; use one per run.

Measured, headless, Godot on the dummy audio driver, an AudioEffectCapture
on Master read as RMS per 0.25 s. The capture is not in the client (I
could not edit it): it is a 20-line Node added to a scratch copy of the
project's SceneShot. Music alone reads 0.0355 (AMBCave x 0.4), with the
default loop 0.0440.
- ROOM2SND + LOOPFIRST, Go, Go: room 1 0.0448; after the Go the log says
  "Player message: room sounds stopped", then "FXRoom2.ogg gain 0.36 loop
  True" and "music FXMusic2.ogg" in the same frame, and the level holds at
  0.1416 for 48 windows (the new track alone would be 0.106, so the loop
  is in it); Go back, 0.0445. The ambient survives the room change.
- miscase: 0.0440 then 0.0355, identical to the control run; the log has
  the one-shot, the loop and the track, no "no such file". missing: "no
  music file for FXGoneMusic.ogg", level 0.0355 before and after, the
  stop finds "loops 0".
- SNDSTOP=loop with LOOPS2: 0.1837 to 0.1659 when FXLoop1 stops (the
  one-shot runs on), to 0.0513 when the one-shot ends (music + FXLoop2,
  which survived). oneshot: 0.1908 to 0.1074 (music + FXLoop1),
  "stopped one-shot fxshot.ogg".
- MUSIC swap: 0.0355 to 0.1066, FXMusic2 alone, so the old track stopped.
  repeat: no second "music" line, level flat. zero: "no music file for",
  level flat.
- WADE=2 and a walk in barinn: depth Depth2, floor 128.0, avatar 102.4,
  three "FXSplash.ogg gain 1.00" lines, RMS pulsing 0.044 to 0.18. Gaps
  between splashes, measured: 599 ms at depth 1, 1544 ms at depth 3 -
  above the 500 ms x depth floor, to frame granularity.
- SNDOWNER: 0.0726 then "dropped 2003 ... its object left" then 0.0355.

`sound-guard.sh <godot> <MobileClient> <resdir> [port]` is the scripted
regression guard for the two name cases, from the client's own
M59SOUNDLOG lines, so it needs no probe: miscase must play all three and
report nothing missing; missing must report the track, start nothing,
and leave the real loop's stop working. It passes on HEAD and FAILS
(miscase, four lines) when M59Sound.OnDisk's case-insensitive backstop
is removed in a scratch copy.

Not closed:
- M59_SNDGHOST found a client difference and I left it. The reference,
  given an id with no object, plays at the origin and keeps the sound in
  its global list until a stop or the next Player (`ControllerSound.cpp:
  393-411,326-335`). M59Sound keeps Owner 9999 and Follow drops the loop
  on the next frame ("dropped 9999 ... its object left"), so the stop
  finds nothing. Whether the origin is audible is a matter of distance.
- Server-104 never sends a loop with an object id (`room.kod:684,715`).
  SNDOWNER is the library's and the reference's shape, not the server's.
- The wading sector is the one under the spawn. barinn's has no server id
  (0), and a SectorChange for 0 reaches every sector without one, so all
  of barinn wades; another room may be narrower. barlmarket not run.
- Levels tell the sounds apart, not their content: they are tones.
  PlayMidi, random pitch/place flags, radius and max volume (sent as 0
  and 100) and a one-shot with an absent id are not covered.
- A full rebuild of the library prints one warning, SYSLIB0014 at
  `Protocol/DownloadHandler.cs:74`; the fake server project has none.

See also: the client -> ../M59Sound.cs | harness.md | parsing notes above
