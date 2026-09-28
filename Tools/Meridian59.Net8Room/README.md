# Meridian59.Net8Room

Draws a ROO's wall geometry as a top-down floor plan PNG using only the
ported core library. Room geometry has to be right before a renderer is
worth writing, and a picture is the cheapest way to check it.

    dotnet run --project Tools/Meridian59.Net8Room -- <file.roo|dir> <outdir> [size]

Dark lines are walls with a sector on one side only; blue lines have a
sector on both, so they are doorways, steps and sector boundaries rather
than barriers.

Orientation: ROO x and y map straight onto image columns and rows, no
flip. This was checked by rendering dvalley1.roo and lining it up
against the wiki map for the same room - it has an off-centre notch, a
distinctive crack and a corridor off one side, so a flip is obvious. A
symmetric room would not have caught it.
