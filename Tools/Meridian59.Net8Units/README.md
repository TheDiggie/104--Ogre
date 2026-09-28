# Meridian59.Net8Units

Checks the two unit conventions the live view depends on, against the
library rather than against a restatement of the same arithmetic.

    dotnet run --project . -- <resourceDir>

**Heights.** `GameView` sets the avatar's height from
`RooFile.GetHeightAt * 0.0625`, stores it in `Position3D.Y`, and the
renderer reads floors as `FloorHeight * 16`. For the camera to sit on
the floor, `GetHeightAt` has to return what `M59Geo.FloorXY` returns.
It does, on every flat sector sampled across all 362 rooms. Sloped
sectors vary across a leaf by design and are excluded from the count,
not from the sampling.

Written the first time against the raw `FloorHeight` field, it failed
on 11,399 of 13,367 samples by a factor of exactly 16 - `GetHeightAt`
already returns room units. That is the finding, not a bug: the chain
`GetHeightAt -> x0.0625 -> Position3D.Y -> x16 -> renderer` closes.

**Kod to room.** `RoomObject.Position3D` is in the server's units.
`M59Geo.KodToWorld` must agree with what `BaseClient.SendReqMoveMessage`
computes independently as `CoordinateX * 16 - 1024` before asking the
ROO for a sector. 20,000 random positions, no disagreement, and the
round trips are stable.

**Handedness.** Nothing inside the renderer can tell you which way round
the world is: mirror the whole view and every texture, sprite and
collision test still agrees with itself. The only external reference in
this repo is the top-down map tool, whose convention - room X to image
columns, room Y to image rows, so +Y runs *down* the map - was checked
against the wiki's own dvalley1 map by lining up an asymmetric notch.

So standing at the origin facing +X is facing right on that map, and the
player's right hand points to +Y. The check places a sprite 600 units to
each side, 2500 ahead, and asserts it is drawn on the matching half of
the screen. It is, in all 46 rooms where the spot is actually visible.

That chains the renderer to the map and the map to something a person
has looked at. Written the first time against a fixed screen column it
failed 50 times out of 50, because 600 units at that distance is
nowhere near three quarters across; scanning for where the sprite
actually landed is what makes it a measurement rather than a guess.
