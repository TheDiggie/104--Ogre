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
