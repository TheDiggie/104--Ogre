# Meridian59.Net8Uv

Checks the renderer's wall texture coordinates against the library's own
`RooWall.GetVertexData`, which is a port of the game's `d3drender.c` and
is therefore the authority.

    dotnet run --project . -- <resourceDir>

This renderer had worked out its own rule by measuring one room: one
texture per grid square, so UV = distance / 1024. That is exactly right
when a texture's size over its shrink factor is 64 - which 128x128
shrink 2 and 64x64 shrink 1 both are, and which is most of what is in
the Barloque inn. It is wrong for everything else, and it was wrong for
903 of 8357 wall middles: a 512x512 at shrink 4 covers twice the wall a
128x128 at shrink 2 does, so those textures were tiled twice as often as
they should be, at half size.

The rule is `along * shrink / textureHeight` across the wall and
`height * shrink / (textureWidth * 16)` up it - the along-wall
coordinate divides by the texture's *height* and the up-wall one by its
*width*, which is the same axis swap this renderer had already found by
looking at pictures.

With that ported: 8357 of 8357 along-wall scales and 7884 of 7884
vertical scales match the library exactly.

Two things the comparison has to allow for:

- `GetVertexData` finishes by insetting the V range one texel at each
  end. That is a bleeding guard for filtered quads in Ogre, not part of
  Meridian's texture mapping, and a point-sampling column renderer does
  not want it. It is undone before comparing.
- `WF_NO_VTILE` makes `GetVertexData` move the geometry rather than the
  UVs - it clips `P0.Z` and drops `P1.Z` by 16 - so a rate comparison
  there is meaningless. Those 266 parts are counted separately. That
  flag is the one piece of the model still unported.
