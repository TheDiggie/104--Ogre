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
