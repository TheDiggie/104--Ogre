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
