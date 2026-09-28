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

Assets are found by `M59Paths.Resolve`, which tries, in order:
`user://resource` (where the Android build unpacks them),
`%LOCALAPPDATA%\Meridian-104\resource`, `%LOCALAPPDATA%\Meridian59\resource`,
`~/.meridian-104/resource`, and a `resource` folder next to the
executable. Setting **Resource Dir** on the root node overrides all of
it. If none of them has any `.roo` or `.bgf` in it the view says so and
lists the paths it tried.

## Scenes

- `FirstPerson.tscn` (the main scene) - first-person textured view.
  WASD to move, arrows or drag to turn.
- `Game.tscn` - the live view: connects to the server and renders the
  room the avatar is in. Moves and turns are sent to the server.
- `Main.tscn` - top-down map: textured floors per BSP leaf, walls over
  the top. T toggles textures, W walls, F refits.

## Controls

Left half of the screen is a floating movement stick - it appears where
your thumb lands. Right half is look: drag to turn. Each tracks its own
finger, so moving and turning at once works, which is why the screen is
split rather than given fixed on-screen buttons.

Tap - a finger down and up without moving - targets whatever you touched.
The renderer picks the sprite under that pixel, opaque texels only and
never through a wall, and that object becomes the library's own target,
which is what the Look / Get / Attack / Use row acts on. Tapping nothing
clears the target and hides the row.

Enter opens the chat line, Escape closes it. ':' emote, '!' yell,
'^' broadcast, '#' guild, anything else say.

On desktop: WASD to move, arrow keys or left-drag to turn, Shift to run.

## Android

Nothing here has been run on a phone yet. What is in place:

- `export_presets.cfg` carries an arm64 Android preset
  (`us.meridian59.mobile`), immersive mode, portrait, internet
  permission, output to `../build/Meridian59.apk`.
- `M59Paths.UnpackIfNeeded` copies `res://resource` out to
  `user://resource` on first run. This is not optional on Android: the
  library reads with `System.IO`, and `res://` inside an APK is an entry
  in the `.pck`, not a file on disk. Godot's `FileAccess` can read it;
  `File.ReadAllBytes` cannot.

What you have to do:

1. Install a JDK 17 and the Android SDK (Android Studio is the easy
   way), then point Godot at them in
   *Editor > Editor Settings > Export > Android*.
2. *Editor > Manage Export Templates* and download the templates for
   your exact Godot version.
3. Copy an installed client's `resource` folder into `MobileClient/resource`.
   It is gitignored - it is hundreds of megabytes and it is not ours.
4. *Project > Export > Android > Export Project*.

The resource folder is the awkward part: a full one is far past the
150 MB the Play Store allows, which is fine for sideloading and not fine
for publishing. The real fix is to download it on first run the way the
desktop patcher does, which is not written yet.

Set the main scene to `Game.tscn` for the live client;
`FirstPerson.tscn` is the offline one and is still the default because
the server path has not been exercised end to end.

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

  Columns are rendered one contiguous band per core. This is checked,
  not assumed: 362 rooms x 8 headings produce byte-identical output
  single-threaded and threaded (`Renderer.Threaded = false` forces the
  old path). On the two cores available in development it is worth
  1.5-1.8x on wall-heavy rooms and nothing at all on barinn, whose cost
  is floor fill and looks memory-bound rather than CPU-bound.

  A phone is several times slower per core, so 480 wide is still the
  sensible default.

  Floor and ceiling fill is 25-60% of a frame (the `NoFlats` and
  `NoSample` knobs on the renderer measure it), and about half of that
  is the sampler. Two attempts to cut it both made things worse and are
  recorded here so nobody repeats them.

  Caching the per-row values a floor plane needs - the distance, the mip
  factor, the fog, which depend on the screen row and the plane height
  and not on the column - is exact and removes three divisions per
  pixel. With one cache slot it took barinn from 7.4 ms to 8.7 ms,
  because the column walk visits several sectors and the height changes
  on nearly every call, so a 540-row table was rebuilt each time. With a
  16-slot keyed cache it went to 16 ms: `FillFlat` is called a few
  hundred thousand times a frame, most calls filling a handful of
  pixels, so the lookup itself cost more than the arithmetic it saved.

  Hoisting the four trig calls out of `FillFlat` into the caller, which
  had already computed exactly those values, changed nothing measurable.

  The lesson is that this function's cost is per call, not per pixel.
  Anything that helps has to draw floors as horizontal spans instead of
  per column, which is a real restructure of the portal walk, and it
  should be done with a profiler and a phone rather than guessed at on
  two noisy cores.

- **Texture aliasing** is handled with mipmaps. Point-sampling a 128x128
  stone texture across a ceiling at a grazing angle produced heavy radial
  streaking; each texture now carries a box-filtered mip chain and the
  sampler picks a level from how much world space a screen pixel covers.
  It costs nothing measurable - smaller levels are kinder to cache, so
  the median frame got very slightly faster.

- **Collision** uses the library's own `RooFile.CanMoveInRoom`, which
  walks the BSP tree. Verified across all 362 rooms: 4320 of 4320
  attempts to walk past the nearest solid wall were blocked, no
  exceptions.

  A blocked move is retried with the movement projected onto the wall
  that stopped it, so you scrape along instead of stopping dead - in
  corridors that is most of the walking you do. One retry only; a corner
  blocks both and chasing it further buys jitter, not a corner. Measured
  rather than eyeballed: holding forward for 600 frames from the middle
  of the inn gets further on 115 of 120 headings, never shorter, and
  never ends up outside the room. Both views share the same
  `WorldSync.TryMove`.
- **Objects** render as camera-facing billboards, sorted back to front
  and occluded by a per-column wall depth buffer. Transparent texels
  (palette index 254) are skipped, and sprite textures deliberately skip
  the mip chain because averaging across them bleeds the cyan key into
  the edges. Verified: five sprites in a line at increasing distance
  scale correctly, and the one placed past the wall is not drawn.

  The frame is chosen per view from the object's facing, so creatures
  turn as you walk around them: a BGF holds a frame set per animation
  group and one frame per direction inside it. Verified by orbiting a
  fixed object - cyclopsX resolves 8 distinct frames around the circle,
  Knight 8, duskrat 6 - and the exported frames are visibly different
  views of the same creature.

  Tap targeting shares the projection with drawing rather than repeating
  it, and is checked against what was actually drawn: 36 scenes across 3
  rooms, 3 creatures and 4 headings, 8.3 million pixels, and every pixel
  a sprite painted is pickable and no pixel it did not paint is. The
  sprites are drawn in a colour the rooms cannot produce for that run, so
  the mask is exact - comparing colours against a bare render had counted
  a sprite pixel as unpainted whenever it happened to match the wall
  behind it.

  Set **Demo Sprite Bgf** on the root node (e.g. `duskrat.bgf`) to
  scatter a few around the room before the server is connected.

- **The server connection exists** but has only been exercised against a
  stub. `M59Client` drives the login handshake; `Game.tscn` / `GameView`
  connects, follows the avatar, and mirrors the server's object list into
  the renderer each frame.

  Credentials come from `M59USER` / `M59PASS` in the environment unless
  set on the node. They are deliberately not stored in the scene.

  An account with more than one character gets a full-screen picker and
  nothing is sent until you choose; one character, or a **Character**
  set on the node, goes straight in. Picking the first silently would
  log you in as the wrong character, which is not a thing to discover
  after the fact.

  Verified over a loopback socket: connects, receives GetLogin, sends
  credentials, receives LoginOK. Everything past that - the character
  handshake, rooms, real objects - needs the live server.

  Movement is client-predicted: the library does not step our own avatar
  (`RoomObject.UpdatePosition` only snaps the avatar to a destination
  something else set), so `GameView` moves it, checks the step against
  `CanMoveInRoom`, then calls `SendReqMoveMessage` / `SendReqTurnMessage`
  and lets the server correct it.

  Note that `RoomObject.Position3D` is in the server's units, not room
  units: the conversion is `(X - 64) * 16`, and height is stored as
  `roomHeight * 0.0625`. `M59Geo.KodToWorld` and friends wrap it so the
  renderer never sees a server coordinate. Taken from the library's own
  arithmetic in `RoomObject.UpdatePosition` and
  `BaseClient.SendReqMoveMessage`; not yet confirmed against a live
  server.

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
