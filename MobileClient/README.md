# MobileClient

Godot 4 (.NET) front end for Meridian 59, built on the ported
`Meridian59` core library. Android and iOS are the targets; the desktop
editor is just where it gets developed.

## Requirements

- Godot **.NET** build - the download labelled "Windows - .NET", file
  name `Godot_v<version>-stable_mono_win64.zip`. The standard build
  cannot run C# at all.
- .NET 8 SDK.

## Running

Open this folder as a project in the Godot .NET editor and press play.
It builds `MobileClient.csproj`, which references
`../Meridian59/net8.csproj`.

Assets are read from `%LOCALAPPDATA%\Meridian-104\resource` unless you
set **Resource Dir** on the root node.

## Scenes

- `FirstPerson.tscn` (the main scene) - first-person textured view.
  WASD to move, arrows or drag to turn.
- `Main.tscn` - top-down map: textured floors per BSP leaf, walls over
  the top. T toggles textures, W walls, F refits.

## How the renderer works

`Renderer.cs` is plain C# with no Godot types. Per screen column it
collects every wall the ray crosses, sorts by distance and walks them
like a Doom-style portal renderer: a one-sided wall - or a two-sided one
that still carries a middle texture - closes the column; otherwise only
the upper and lower steps are drawn, the window narrows, and the ray
carries on. Floors and ceilings fill the rest.

It has no Godot dependency on purpose: `Tools/Meridian59.Net8Fpv`
renders the identical code to a PNG, so the output can be checked
against a reference image rather than eyeballed in motion. The two were
confirmed byte-identical.

## Known limits

- **Speed.** A uniform spatial grid (`WallGrid.cs`) means a ray only
  tests walls in the cells it crosses. Verified exhaustively: all 362
  rooms render **pixel-identical** to testing every wall, 12 camera
  angles each.

  Desktop x64, release, renderer only:

  | | median | worst | worst room |
  |---|--------|-------|------------|
  | 480x270 | 0.81 ms | 4.6 ms (218 fps) | a5 |
  | 960x540 | 2.86 ms | 13.4 ms (75 fps) | d6e6ulake |

  The grid is worth 3-14x on wall-heavy rooms - GreenPlantation's 6540
  walls went 38.1 ms to 2.8 ms. At 960x540 the bottleneck has moved off
  walls entirely and onto per-pixel floor and ceiling filling, which is
  why a 195-wall guild hall is now among the slowest. That is where the
  next optimisation belongs, not in the caster.

  A phone is several times slower, so 480 wide is still the sensible
  default.

- **Texture aliasing** is handled with mipmaps. Point-sampling a 128x128
  stone texture across a ceiling at a grazing angle produced heavy radial
  streaking; each texture now carries a box-filtered mip chain and the
  sampler picks a level from how much world space a screen pixel covers.
  It costs nothing measurable - smaller levels are kinder to cache, so
  the median frame got very slightly faster.

- **Collision** uses the library's own `RooFile.CanMoveInRoom`, which
  walks the BSP tree. Verified across all 362 rooms: 4320 of 4320
  attempts to walk past the nearest solid wall were blocked, no
  exceptions. There is no sliding along walls yet - a blocked move is
  simply rejected.
- **Objects** render as camera-facing billboards, sorted back to front
  and occluded by a per-column wall depth buffer. Transparent texels
  (palette index 254) are skipped, and sprite textures deliberately skip
  the mip chain because averaging across them bleeds the cyan key into
  the edges. Verified: five sprites in a line at increasing distance
  scale correctly, and the one placed past the wall is not drawn.

  There is nothing to populate the list yet - real objects come from the
  server. Set **Demo Sprite Bgf** on the root node (e.g. `duskrat.bgf`)
  to scatter a few around the room in the meantime.

- **The server connection exists** but has only been exercised against a
  stub. `M59Client` drives the login handshake; `Game.tscn` / `GameView`
  connects, follows the avatar, and mirrors the server's object list into
  the renderer each frame.

  Credentials come from `M59USER` / `M59PASS` in the environment unless
  set on the node. They are deliberately not stored in the scene.

  Verified over a loopback socket: connects, receives GetLogin, sends
  credentials, receives LoginOK. Everything past that - the character
  handshake, rooms, real objects - needs the live server.

  `Tools/Meridian59.Net8Play` runs the same client headlessly and prints
  what arrives, which separates a networking problem from a rendering
  one.
- Transparent middle textures on two-sided walls are treated as solid.

## Unit conventions

Established by measurement, not documentation - see
`Tools/Meridian59.Net8RoomTex/README.md`:

- Wall X/Y are FINENESS units, 1024 per grid square.
- Sector heights and `ClientLength` are in XY/16 units (measured:
  `xyLength / ClientLength == 16.0` exactly across barinn.roo).
- One texture tiles across one grid square, so UV = xy / 1024.
