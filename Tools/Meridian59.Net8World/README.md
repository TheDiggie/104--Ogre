# Meridian59.Net8World

Drives `WorldSync` - the part of the live client sitting between the
server's data model and the renderer - with hand-built `RoomObject`s. No
socket, no protocol, no engine.

    dotnet run --project . -- <resourceDir>

That boundary is where this repo's code is most likely to be wrong: it is
where the server's units meet the renderer's, and a wrong sign there is a
wall you walk through. The library's own message handling has been in
production for years and is not what is under test.

Checked:

- A room change rebuilds the renderer; the same room does not. The
  comparison is by reference, because the library hands back the same
  `RooFile` instance for the same room.
- The object list becomes sprites: your own avatar and anything with no
  art are skipped, each sprite carries the object it stands for so a tap
  can be turned back into a target, and the coordinates convert
  `(kod - 64) * 16` for position and `x16` for height. A null list
  empties the sprites rather than leaving the last frame's.
- Every step is exactly one step's length, and lands on the floor rather
  than inside or above it.
- A blocked step leaves the avatar exactly where it was.
- Walking 120 headings from the middle of the Barloque inn until the room
  refuses: all 120 stop at a wall and none of them ends up outside the
  room, which is what a units disagreement between the step and the
  collision check would look like.
- Run over walk matches the server's own speed constants.
