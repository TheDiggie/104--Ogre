# Meridian59.Net8RoomTex

Renders a room's floor with its real textures to a PNG, using the same
UV maths as `MobileClient/RoomView.cs`. It exists so the texture
pipeline can be checked by eye without running Godot.

    dotnet run --project Tools/Meridian59.Net8RoomTex -- <resourceDir> <room.roo> <out.png> [size]

Two things it pinned down that are not obvious from the code:

- **Texture scale.** Rooms are in FINENESS units (1024 per grid square)
  and one texture tiles across one square, so UV = world / 1024. Derived
  from barinn.roo spanning exactly 16 squares, not from documentation.
- **RooSubSector.Sector is always null.** The loader allocates the field
  but never assigns it, so the sector has to be looked up by the 1-based
  `SectorNum`. Anything reading `leaf.Sector` directly gets a
  NullReferenceException - including `RooFile.VerifySight`.
