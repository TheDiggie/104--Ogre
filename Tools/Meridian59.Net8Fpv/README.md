# Meridian59.Net8Fpv

Renders a first-person view of a room to a PNG using the **same**
`Renderer` the Godot client uses - the file is compiled in by link, not
copied - so the image here is the game's output, checkable without
running Godot.

    dotnet run --project Tools/Meridian59.Net8Fpv -- <resourceDir> <room.roo> <out.png> [x y angleDeg] [w h]

With no camera arguments it stands in the middle of the room's largest
BSP leaf facing along +X.

Confirmed against barinn.roo: stone walls with wooden wainscoting, a
heraldic banner, red carpet over a tiled floor, timbered ceiling -
960/960 columns closed.

One bug this tool caught that would have been very hard to see in
motion: the wall side test was inverted, so every ray read the sector on
the wrong side of the wall. Only 234 of 960 columns ever closed and the
room rendered as mostly black. Flipping the sign fixed it.
