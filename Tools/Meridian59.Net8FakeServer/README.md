# Meridian59.Net8FakeServer

A Meridian 59 server that is not one: just enough of the protocol to walk
a real client from its first byte to standing in a room with things in it.

    dotnet run -c Release -- [port] [resourceDir] [room.roo]

Why it exists: the live half of this client - login, character select,
room entry, the object list, chat - could not be exercised at all. The
real server is not reachable from here, so that whole path was written by
reading the library and never once run. This makes it runnable on
loopback, with no account and nothing at stake.

Drive it with either client:

    # headless
    M59USER=tester M59PASS=x dotnet run -c Release \
      --project Tools/Meridian59.Net8Play -- /tmp/res 127.0.0.1 15999 6

    # the Godot one, photographed
    M59USER=tester M59PASS=x xvfb-run -a <godot> --path MobileClient \
      --resolution 900x1600 SceneShot.tscn -- \
      --out live.png --res /tmp/res --host 127.0.0.1 --port 15999 --wait 300

What it answers, and what each step is for:

| it sends | because |
|---|---|
| `GetLogin` | opens the conversation; the client replies `Login` |
| `LoginOK` | accepts any name and password |
| `GetChoice` | carries the hash table; the client replies `ReqGame` |
| `GameState` | **moves the client's parser from login mode into game mode** |
| `Characters` | one character, "Tester"; the client replies `UseCharacter` |
| `Player` | which room, and which object in it is you |
| `RoomContents` | the avatar and three duskrats |
| `Said` | a line of chat, on entry and whenever you say something |

Three things it taught, none of which were obvious from reading:

**`GameState` is the hinge.** Without it the client's own parser stays in
login mode and reads game-mode messages as login ones. It is sent in
answer to `ReqGame`, which the client sends in answer to `GetChoice`.

**The client verifies no CRC on what it receives.** It signs what it
sends, and `CRCCreatorEnabled` only affects outgoing packets - so a fake
server needs none of the packet crypto.

**Chat is not text on the wire.** The server sends a string resource id
and the client looks it up, which is why this server writes an `.rsb`
string file into the resource folder first: room name, object names, and
the two lines it is able to say. If the folder already has one, it uses
that instead and says so.

A trap worth knowing: `RoomObject`'s constructor types its angle `ushort`
but stores it straight into a float field that is read back through
`RadianToBinaryAngle`. It is radians. Passing a binary angle - 512, say -
overflows on the way out and the send fails.
