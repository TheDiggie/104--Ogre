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
times settled frames with sprites, test on and off.
