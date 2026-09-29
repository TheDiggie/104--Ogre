# Wire format traps

The library's own encoders and decoders are the truth here. These are the
places where the obvious reading of a field is wrong, each one found by
something not working.

## ObjectFlags fields that are their own bytes
Tags: gotchas, architecture | Player, Drawing, Minimap and NameColor are separate bytes on the wire, not bits in the Flags uint - the constructor's Flags argument does not set them

`ObjectFlags` looks like a bitfield and mostly is, but `Player`,
`Drawing`, `Minimap` and `NameColor` are written as their own bytes. A
fixture that passes a `Flags` uint alone produces objects the client
renders wrongly - no name colour, no minimap dot - with nothing in any
log to say so.

See also: fixtures -> fake-server.md | name colours -> mobile-client.md

## ObjectID is 4 or 8 bytes
Tags: gotchas | The top 4 bits of an ObjectID flag that a count follows; ByteLength is 4 or 8, and a raw uint comparison fails once counts are attached

The top four bits are a flag meaning a count follows the id, so
`ByteLength` is either 4 or 8. Comparing a raw uint against an id works
until the server attaches a count to it, and then silently stops
matching. Use the library's own comparison.

See also: stacked items -> mobile-client.md

## Message ids in use
Tags: architecture | ReqAttack 103, ReqCast 105, SayTo 110, SayGroup 111 - a tell is sent as SendSayGroupMessage

A tell is not its own message: it goes out as `SendSayGroupMessage`
(SayGroup = 111). `ChatCommand.Parse` only yields a Say for text
beginning "say", "s" or "sagen", which is why a bare line of text used to
send nothing at all.

See also: the chat bar -> mobile-client.md

## The build is non-VANILLA
Tags: architecture | NameColors, the MM_* constants, preferences and the MeridianDate offsets all differ between VANILLA and this build

Read the non-VANILLA branch of any conditional in the library. Reading
the other one gives answers that are self-consistent and wrong.

See also: the one law -> ../../CLAUDE.md
