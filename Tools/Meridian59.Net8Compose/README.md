# Meridian59.Net8Compose

Checks `MobileClient/M59Compose.cs`, which builds an object's picture the
way the game builds it - main overlay plus the suboverlays pinned to its
hotspots - against things that can be known without a server.

    dotnet run -c Release [resourcedir]      # the checks, default /tmp/res
    dotnet run -c Release -- crush [dir]     # how much art cannot be decoded here
    dotnet run -c Release -- probe           # frames, groups and hotspots of one file

What the checks say, and why each one is worth running:

**Plain objects compose to their own frame.** An object with no parts and
no frame offsets must come out pixel-for-pixel identical to the single
frame the client drew before. This is the regression guard: composition
must not quietly resize everything that was already right. 404 of the 558
files to hand qualify; the other 142 carry an offset or are clamped by the
quality cap, and those are the objects the old path placed or sized
wrongly.

**A part lands where RenderInfo puts it.** A part hung on a hotspot must
appear, with pixels, inside the box the library gives for it, and the
picture must grow to hold it rather than clip it. The frame and angle are
searched for rather than assumed - not every frame carries every hotspot,
and an arm turned away from you has none.

**Overlays win the overlap, underlays lose it.** The sign of the hotspot
decides whether a part is drawn over the main frame or behind it, and
this tests the outcome rather than the flag: in the overlap, where the
main frame is already opaque, an underlay must change *nothing at all*,
which is the exact half of the test. The part used there is a copy of the
main frame's own art - a fixture, not anything the game composes - purely
because a real weapon covers too few pixels of a hand to tell "behind"
from "slightly to the left". That also makes the overlay half inexact,
since it repaints a good share of what it covers in the same colour.

`sheet` draws the real pairings instead: the arm alone, then the arm
holding an axe, a sword and a maul.

`crush` counts what this client cannot draw. Frames are compressed one of
two ways, and CRUSH can only be undone by a proprietary `crush32.dll` on
x86 Windows - 201 frames, 2.93%, all of them in nine files, every frame
in each: the dragons, two beetles and `ranu`. Those creatures are
invisible here and would be on a phone. See the README in `MobileClient`.
