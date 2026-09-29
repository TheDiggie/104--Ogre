# The fake server

`Tools/Meridian59.Net8FakeServer` stands in for Server 104 so the client
can be played without a live game. It is also the single largest source
of false bug reports in this port.

## Check the fixture before blaming the client
Tags: lessons, process | Spell art, quest art, the EQUIPPED flag, picking things up, buying and the wizard's face parts each looked like missing client code and were missing test data

The rule that came out of it: when a feature looks unimplemented, prove
the server is sending the data before reading a line of client code.

See also: ../../CLAUDE.md | harness.md

## It dies, and a dead server looks like a broken client
Tags: gotchas | A run against a dead fake server returns a flat grey frame, because the client never connected - pgrep it and read head -3 /tmp/fake.log first

See also: harness.md

## Parsing notes
Tags: gotchas | ReqBuyItemsMessage parses a whole TCP message and throws on a body; hand-parse PI, seller, ushort count, ObjectIDs. An offer names the partner first, a counter-offer does not

`ReqBuyItemsMessage(body)` throws `WrongLEN - input:256 expected:8061`.
Reading a counter-offer as an offer gives a nonsense count and prints
nothing, which reads as "trade is broken".

See also: wire-format.md
