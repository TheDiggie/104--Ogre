# Meridian59.Net8RoomTex

Renders a room's floor with its real textures to a PNG, using the same
UV maths as `MobileClient/RoomView.cs`. It exists so the texture
pipeline can be checked by eye without running Godot.

    dotnet run --project Tools/Meridian59.Net8RoomTex -- <resourceDir> <room.roo> <out.png> [size]

Two things it pinned down that are not obvious from the code:

- **Texture scale.** Rooms are in FINENESS units (1024 per grid square)
  and one texture tiles across one square, so UV = world / 1024. Derived
  from barinn.roo spanning exactly 16 squares, not from documentation.
- **Texture orientation.** Room textures are stored with their axes
  swapped relative to how they decode as an image: texture X runs up a
  wall and along world Y. Checked against grd11065 (a panelled door) and
  grd02011 (a floor of tall stone slabs).
