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

## One trigger, instead of a timing hack per switch
Tags: process | Several of these have to land at a MOMENT - while a panel is open, while a popup is armed - and two audits each built their own; there is one mechanism now, and it still counts CLIENT MESSAGES

`M59_FIRE=<n>:<cmd>[,<n>:<cmd>...]` runs a command n client messages in.
`M59_TRIGGER=<path>` polls a file every 200 ms and runs each line appended
to it at the next client message, so the send happens on the session's own
thread, in order with everything else. `watch.sh <clientlog> <triggerfile>
<pattern> <command> [...]` is the log-watching half: it appends a line the
moment a pattern appears in the client's log, so "while the loot window is
open" is `"pressed Activate" "loot remove"` and nothing else.

The counting convention is KEPT, not replaced: pings outpace frames under
load, so a millisecond deadline fires before the frame that was supposed to
show the result. `M59_GUILD_ASK=n` and `M59_LOOT_AFTER=n` are folded into the
same plan rather than carrying their own timers.

Commands: `info`, `list`, `halls [n]`, `ask`, `shield`, `shielderr`,
`samples`, `prefs`, `bag`, `loot [mode]`, `uselist`, `use <id>`,
`unuse <id>`, `change [mode] [id]`, `say`, `env NAME=VALUE`.

`env` works because the switches in this block are read when they are USED,
not once at start - so `env M59_GUILD=member` then `info` changes rank
mid-session. The older switches above are `static readonly` on purpose and
`env` cannot touch them.

Seen: `watch.sh` + `Activate` + `loot remove` took the contents window from
three rows to two while it was open; `M59_FIRE="30:env M59_GUILD=member,48:info"`
sent a master's word and then a member's in one session.

See also: harness.md | the client -> mobile-client.md

## The guild surface: everything past reading the roster was unreachable
Tags: process, lessons | The fixture answered ReqGuildInfo and ReqGuildList and nothing else, always as a guildmaster, with a flag word that set RENOUNCE and DISBAND at once - which no rank ever holds, and which hid all five bugs in fa28d39

Confirmed in Program.cs before building: no GuildAsk, no GuildHalls, no
shield of any kind, no answer to ClaimShield, one hard-coded flag word, one
hard-coded roster, one hard-coded vote.

The flag word is the heart of it. Every guild command is an object with a
`viRank_needed`, and `ResetCommand` adds its bit at or above that rank and
removes it below (`guildcmd.kod:72-95`); RENOUNCE is the exception, and
`gcrennce.kod:57-69` REMOVES it at RANK_MASTER and adds it at every other
rank. So a master has DISBAND and never RENOUNCE, and everybody else the
reverse - the old word was impossible, and a panel tested against it is a
panel tested against nothing.

- M59_GUILD=member|lieutenant|master|solo|none|both. The word is built from
  the real GCID_* bits (`blakston.khd:2956-2971`) at each command's own
  threshold: member 0x0024 (VOTE|RENOUNCE), lieutenant adds EXILE, SET_RANK,
  ABANDON_HALL and the four diplomacy rights, master 0xFFE3 - everything but
  RENOUNCE. `solo` is a master whose roster is one name; `none` sends no
  GuildInfo at all, only the line the server sends instead
  (`user.kod:2749-2753`). `both` is the old word, kept so a run can show what
  the earlier audits were looking at; unset behaves as `both`.
- M59_GUILD_HALL=0|1. The hall is NOT a field on the wire: the password byte
  and the chest password go out only to a RANK_MASTER of a guild that has one
  (`user.kod:2756-2765`), and that byte is the client's whole evidence - which
  is why every other rank was told the guild had no hall (fa28d39).
- M59_GUILD_FLAGS=<0x..|n> overrides the word after the rank has chosen one:
  the four diplomacy rights alone, or a set with VOTE clear.
- M59_GUILD_VOTE=<name|none> names the supported member (`user.kod:2775-2790`).
  Unset is Alice, and nobody when she is not on the roster.
- M59_GUILD_HALLS=<n>: ReqGuildList answers with a GuildHalls list of n halls
  instead of the diplomacy list. Nothing the client sends asks for this list -
  the real one comes unasked from a broker (`user.kod:7670-7700`) - and n=0 is
  the legitimate empty answer that used to close the window in silence.
- M59_GUILD_ASK=<n> pushes the two prices a guild costs
  (`user.kod:7703-7712`), which is the unasked arrival that lands on top of
  whatever is open.
- M59_GUILD_SHIELD=1 answers the designer's sample list every time and the
  shield itself ONCE, so a second open shows what the panel kept.
- A ClaimShield (9B-21) is answered with a GuildShield naming the owning
  guild, or 0 for an unclaimed design, and really-claim takes it
  (`user.kod:2627-2659`). M59_SHIELD_ERROR=1 answers with GuildShieldError
  instead - the real server has no error on this path, so that one is the
  library's class and the reference's case, not kod's.
- M59_UCLOG=1 prints every UserCommand body as hex, so the ids a flow puts on
  the wire are readable without a capture.

Seen, each on a real client: member - roster, Renounce, no hall label, no
password UI; master with a hall - the chest password box filled with "rats",
Set password, Abandon hall, Disband; master without - "No guild hall." and
neither. `none` prints the line in chat and never opens the panel. Halls 3
lists three named halls with cost and daily rent (the server writes 24x the
rent value); halls 0 opens the window saying none are on offer with Buy
disabled. A solo master's Disband put 9B-14 on the wire. The shield designer
claimed a design (owner 0, then 9001 after really=1) and met silence on the
second ask. The GuildAsk push opened Create Guild over the say box - which is
the one divergence fa28d39 left open.

See also: the panels -> mobile-client.md | harness.md

## Using things, and lists that change under an open window
Tags: process, lessons | No ReqUse, no ReqUnuse, no UseList, so IsInUse was false client-side for the whole session - the single biggest hole in the pack fixture, and the whole wield/unwield half of the inventory sat behind it

`Carry`'s `inUse` argument is not on the wire at all; only the EQUIPPED flag
is, and that is a different thing. Without a UseList nothing is ever in use,
so the button said Use forever and the bug in 07f2920 could not be reached.

- M59_USE=1: ReqUse is answered with Use, ReqUnuse with Unuse, and
  ReqInventory carries a UseList of what is already worn. ReqUse, ReqUnuse
  and ReqApply are LOGGED with their ids whether or not the switch is set -
  "did the press reach the wire" is the first question a use bug asks, and
  "game-mode 106, ignored" is a poor answer. ReqApply is not answered: the
  real server applies the item and the result is whatever the item does, not
  a protocol reply.
- M59_USE_LIST=<ids|none>, default 8001 (the axe).
- M59_USE_CHANGE=rename|applyable|flags: the item changes IN PLACE the moment
  it is worn - a new name, the APPLYABLE bit through a full ObjectUpdate, or
  the same bit through ChangeObjectFlags. The library mutates the same
  InventoryObject for all three (`DataController.cs:2288-2292`, `:2139-2143`),
  which is exactly what the caption depends on. The third is a Server-104
  packet the client compiles only outside VANILLA and OPENMERIDIAN; this build
  defines neither, and it was seen working.
- M59_LOOT=counts|shrink|empty|remove, on the trigger or M59_LOOT_AFTER
  messages in: the container's whole list, re-sent changed. Both loot bugs in
  6970025 need that.
- M59_FLOOREQUIP=1 puts OF_EQUIPPED on the floor coin and on the axe inside
  the container. Server-104 never sends the bit - kod keeps 0x8000 free
  (`blakston.khd:2739`) - it is the library's and the other flavours'
  (`ObjectFlags.cs:65`).

Seen: with USE_LIST=8001 the bag opens with the axe already reading "Unuse";
with USE_LIST=none a press sends ReqUse 8001, the server answers Use and a
Change, and the slot reads "a blessed nerudite axe" / "Unuse" - the whole
chain. `flags` turned the caption to "Apply" through ChangeObjectFlags alone.
The contents window reads "a nerudite axe (in use)" with FLOOREQUIP set and
not without. The floor LIST shows no suffix either way, which is right:
`UILootList.cpp:192` sets the plain name where `UIObjectContents.cpp:191-194`
appends it.

See also: the bag -> mobile-client.md | the loot window -> mobile-client.md

## Preferences and passwords: the seven switches were dead every session
Tags: process, lessons | UC_REQ_PREFERENCES was never answered, so PreferencesFlags.Enabled stayed false and all seven server preferences were disabled all session; BP_CHANGE_PASSWORD stopped at the wire

- M59_PREFS=1 answers both the request and a set with ReceivePreferences
  carrying the stored word. A set ENDS in the same reply on the real server
  (`user.kod:2405-2409`), which is how the client learns a bit was refused.
- M59_PREFS_WORD=<0x..|n>, default 0x7E - CF_DEFAULT_PREF (`blakston.khd:80`).
- M59_PREFS_REFUSE=<mask>: bits masked out of what you send before the echo.
- M59_PREFS_DELAY=<ms> holds the FIRST answer back. The client asks the moment
  the character is accepted and again after login (`BaseClient.cs:565-572`,
  `:2765-2772`), so the window where the status bar has no word to read is a
  real one and this is how wide it gets.
- M59_PASSWORD=ok|bad answers BP_CHANGE_PASSWORD with BP_PASSWORD_OK or
  BP_PASSWORD_NOT_OK and nothing else, as `blakserv/game.c:536-566` does.

Seen: with M59_PREFS=1 the Settings panel's seven boxes are live and the
status bar reads "safety on" from the word; starting at 0x3E and refusing
0x40, the client asked for 0x7E, the server granted 0x3E and echoed it. The
password pair was driven on raw bytes rather than through the client -
SceneShot's `@type` fills only the FIRST visible box and the panel has three,
so the form cannot be filled from a scripted run - and 160/161 came back for
ok/bad.

Not closed:
- The password form itself is still undriveable by the harness; only the wire
  was exercised. A `@name:<LineEdit>=text` step in SceneShot would close it.
- M59_PREFS_DELAY's race was not photographed inside its own window: the shot
  comes 150 frames in, long after a 4 s delay has passed. The server line
  "(holding the preference word back N ms)" and the disabled/enabled boxes are
  the evidence; a shot inside the window needs `--shots` and a step count.
- The member view still shows an Exile control per row with EXILE clear in the
  word. Whether it is merely disabled was not established - it is a client
  question, and the switch is what makes it askable.
- M59_GUILD=none leaves the Guild button on the bottom row, which now opens
  nothing. Also a client question the switch newly exposes.

Every switch here is off unless set. Control: scenes entry plus
Book/Close/Bag/Close/Me/Close against the build before and after gives an
identical server log (47 non-ping lines, byte-for-byte) and an identical
frame apart from the debug overlay's clock and frame counter.

See also: the options panel -> mobile-client.md | harness.md
