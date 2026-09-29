# Running it

Nothing in `MobileClient` has run on a phone, and nothing has been near
the real Server 104. What has changed is everything in between: there is
a Godot in the environment now, and a fake server, so every panel has
been opened, driven and photographed, and what reaches the wire has been
read off a log rather than assumed. Section 2a is that harness, and it
is the fastest way to see whether something works.

What that still does not prove: a real server's data, a real phone's
screen and thumbs, and the whole Android export path.

This is the order that finds problems fastest.

## 1. The checks that need no engine

From the repo root, with `<res>` being an installed client's `resource`
folder:

    dotnet run -c Release --project Tools/Meridian59.Net8Units       -- <res>
    dotnet run -c Release --project Tools/Meridian59.Net8World       -- <res>
    dotnet run -c Release --project Tools/Meridian59.Net8Uv          -- <res>
    dotnet run -c Release --project Tools/Meridian59.Net8RenderCheck -- all <res>

Each ends in `OK`. The last one takes a few minutes; it renders all 362
rooms twice, then 8.3 million pixels of sprite picking, then the grate
ordering and the slope algebra.

What each is for, briefly:

- **Units** - the height chain and the server-to-room coordinate
  conversion, against the library's own second implementation.
- **World** - the live client's data-model-to-renderer half, driven by
  hand-built objects with no socket and no engine.
- **Uv** - wall texture coordinates against `RooWall.GetVertexData`,
  which is a port of the game's own `d3drender.c`.
- **RenderCheck** - threaded output identical to single-threaded, tap
  picking identical to what was drawn, grates covering what is behind
  them and not what is in front, and the sloped-floor solve against a
  bisection.

    dotnet run -c Release --project Tools/Meridian59.Net8Fpv \
        -- <res> barinn.roo shot.png --sprite duskrat.bgf

writes a PNG of the Barloque inn with rats in it. If that looks right,
the renderer is fine on your machine and anything wrong afterwards is
the engine layer.

## 2. The offline view, on the desktop

Each widget is built inside a try/catch, so one that throws costs you
that widget rather than the view, and the failure goes into the status
text. That was written when none of them had ever executed; they all
have now (section 2a), but the guard has earned its keep and stays.


Open `MobileClient` in the Godot **.NET** editor and press play.
`FirstPerson.tscn` is the main scene and needs no server or account.

Expect: the inn, a **Rooms** button top right that lists every room with
a filter box, WASD and drag-to-turn on the desktop, and a floating stick
under your left thumb on a touchscreen.

Worth trying deliberately: walk into a wall at a slight angle. You
should scrape along it rather than stop dead.

## 2a. The fake server and the screenshot harness

This is where most of the work gets checked. Two pieces:

    dotnet run -c Release --project Tools/Meridian59.Net8FakeServer -- 15999 <res>

stands up something that speaks enough of the protocol to log you in,
put you in the Barloque inn with rats, players, a shop, a container, a
loot pile, mail, quests, a guild and a trade offer, and to log what you
send it. It is the only place the wire can be read, and reading it is
the point: a screen saying the right thing is not evidence that the
right thing was sent. Several bugs this repo has had looked correct on
screen and were wrong on the wire, and one looked wrong on screen and
was right.

Switches: `M59_STATCHANGE=1` for the stat-change wizard, `M59_NEWS=1`
for the news panel, `M59_CHATFLOOD=1` to fill the chat log.

Then `SceneShot.tscn` drives the client and photographs it:

    godot --path MobileClient SceneShot.tscn -- \
        --out shot.png --res <res> --host 127.0.0.1 --char Tester \
        --shots --press "Bag,@slot,Book,Close" --slot 1 --wait 140

`--press` is a comma-separated path through the interface. Besides a
button's own caption, a step can be `@name:<NodeName>` (rows and icons
have names; a bound spell shows a picture and no text), `@hold:<Node>`
for a press held down (several things are bound to a hold), `@tap:640x1050`
for a tap at a point, `@slot` for an inventory slot, and `@type` /
`@submit` for the text box. `--shots` writes `shot-1.png`, `shot-2.png`
and so on, one after every step.

Two habits worth copying, both learned the hard way. Stack the same crop
from several frames into one image and look at them together - a
sequence where step 5 should leave the world exactly as step 1 did will
show you the difference immediately, and three real bugs came out of
that. And when something looks missing, look at the *whole* frame before
believing it: twice it was simply outside the crop.

## 3. The live view

Set the main scene to `Game.tscn`, put `M59USER` and `M59PASS` in the
environment, and play.

The first thing likely to stop you is the version. The client reports
5.0, and if Server 104 wants something else it answers with GetClient
and asks for a patch - the status text says so, with the version it
offered. **Version Major** and **Version Minor** on the node change it
without a rebuild.

Everything after `LoginOK` - the character handshake, the room arriving,
objects, movement going back, chat, trading, buying, looting - has been
run against the fake server in section 2a and photographed. What no
fixture can stand in for is the real server's data: its resource files,
its string table, its room geometry and its own idea of the protocol
version. That is what running this finds out.

If the world comes up blank or untextured, suspect the setup before the
renderer. Four things in this path each fail silently rather than with
an error, and all four are now handled but worth knowing about: the
resource folder layout (flat versus subfolders), the order the resource
path is set in, which string file is used, and whether the client's
clock is being advanced. The Godot console prints the resource folder
and the string file it settled on.

If it fails, the status text in the top left carries the last few server
notices, and

    dotnet run -c Release --project Tools/Meridian59.Net8Play

runs the same client with no engine at all and prints what arrives,
which separates a networking problem from a rendering one.

## 4. Android

See the Android section of `README.md`. You need a JDK 17, the Android
SDK, export templates for your exact Godot version, and an installed
client's `resource` folder copied into `MobileClient/resource`.

## What to send back

The status text, or whatever `Net8Play` printed. A screenshot beats a
description for anything that renders - the last three orientation bugs
were all caught by looking at one.
