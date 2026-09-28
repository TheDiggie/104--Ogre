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
