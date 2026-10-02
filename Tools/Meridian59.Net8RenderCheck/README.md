# Meridian59.Net8RenderCheck

Two exhaustive checks on the renderer, both against what it actually
draws rather than against a second copy of its arithmetic.

    dotnet run --project . -- threads <resourceDir>
    dotnet run --project . -- pick    <resourceDir>
    dotnet run --project . -- all     <resourceDir>

**threads** renders all 362 rooms from 8 headings single-threaded and
threaded and compares byte for byte, including the solid column count.
Columns are independent so this should hold; it is checked rather than
assumed, because the scratch state that stopped it holding - the hit
list and the wall grid's visit stamp - used to be fields.

**pick** asserts that every pixel a sprite painted is pickable and no
pixel it did not paint is, over 36 scenes and 8.3 million pixels. The
sprites are drawn in a colour the rooms cannot produce, so the mask is
exact: comparing a normal render against a bare one reported 1893 false
hits, all of them sprite pixels that happened to match the wall behind
them.

**flatdepth** (`-- flatdepth <resourceDir>`) is the floors' half of
what `lintel` is for walls. The sprite pass used to test a sprite
against the wall that closed its column and the window the walk left
open past each upper and lower part, and against no floor or ceiling
at all - so a creature on the lower floor behind a raised platform
had its legs painted over the platform's top. The renderer now keeps
a per-pixel flat depth (`Renderer._flatDepth`) and tests sprites,
labels and taps against it. The check renders the same scenes with
the test on and off and counts what it takes back (83748 of 12.1
million sprite pixels across 720 scenes when it was written), and -
the part that fails if the row conventions ever drift - puts
creatures on the level floor of the eye's own BSP leaf, where nothing
can be nearer than they are, and requires that the test takes back
exactly nothing: 0 of 3.1 million pixels across 705 scenes. It also
times settled frames with sprites, test on and off. The feet case
skips depth sectors (669 scenes since): the game does not stand a
creature on a water floor, it sinks it - that is `wade`.

**wade** (`-- wade <resourceDir>`) is what the flat depth does to a
creature in water. A sector flagged shallow, deep or very deep
(`RooSectorFlags.cs:35-38`) stands its objects FINENESS/5, 2/5 or 3/5
under its floor (`RooFile.cs:120-127`, `RooSector.cs:813-841`,
`RoomObject.cs:1081-1082`), the reference draws them at that height
(`RemoteNode.cpp:510-515`) with the camera on the same node
(`:406-425`), and the water surface - the floor at its undepthed
height - writes depth like any flat (`general.material:414-445`), so
a wader shows from the waterline up. The check plants a tagged
creature where the game does, the eye wading too, solves the row the
surface crosses it on from the camera and the sector alone, and
requires three things: no tagged pixel more than a row under that
line with the clip on (0 of them), the clip-on and clip-off renders
identical above it (0 lost, 0 gained - the billboard is wholly inside
the eye's own convex leaf, so the water is the only nearer flat), and
the water hiding SOMETHING (37779 of 171353 sprite pixels across 96
scenes in 21 rooms when written). Switching the clip off for the
tight render fails it in every room. A depth sector whose floor
texture the resource set lacks is skipped and counted (1565 of 1689
here - this fixture set is missing most of the water art, grd08895
and the 8911 family among them): the reference builds no floor for
it either (`ControllerRoom.cpp:789-790`), so a creature standing in
it IS drawn through the hole, in both clients.
