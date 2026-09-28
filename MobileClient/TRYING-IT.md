# Running it

Nothing in `MobileClient` has run on a phone, and the overlays have never
run at all - there is no Godot in the environment they were written in,
so what is proven about them is that they compile against the real
`GodotSharp`. The renderer underneath them is a different story: it is
checked exhaustively by the tools in `Tools/`, which need no engine.

This is the order that finds problems fastest.

## 1. The checks that need no engine

From the repo root, with `<res>` being an installed client's `resource`
folder:

    dotnet run -c Release --project Tools/Meridian59.Net8Units       -- <res>
    dotnet run -c Release --project Tools/Meridian59.Net8World       -- <res>
    dotnet run -c Release --project Tools/Meridian59.Net8RenderCheck -- all <res>

Each ends in `OK`. The last one takes a few minutes; it renders all 362
rooms twice and then 8.3 million pixels of sprite picking.

    dotnet run -c Release --project Tools/Meridian59.Net8Fpv \
        -- <res> barinn.roo shot.png --sprite duskrat.bgf

writes a PNG of the Barloque inn with rats in it. If that looks right,
the renderer is fine on your machine and anything wrong afterwards is
the engine layer.

## 2. The offline view, on the desktop

Open `MobileClient` in the Godot **.NET** editor and press play.
`FirstPerson.tscn` is the main scene and needs no server or account.

Expect: the inn, a **Rooms** button top right that lists every room with
a filter box, WASD and drag-to-turn on the desktop, and a floating stick
under your left thumb on a touchscreen.

Worth trying deliberately: walk into a wall at a slight angle. You
should scrape along it rather than stop dead.

## 3. The live view

Set the main scene to `Game.tscn`, put `M59USER` and `M59PASS` in the
environment, and play.

This is the part that has only ever been run against a stub, and it
stops being guesswork the moment you run it. Everything after `LoginOK`
is untested: the character handshake, the room arriving, real objects,
and movement going back to the server.

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
