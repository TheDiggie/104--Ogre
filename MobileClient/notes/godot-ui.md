# Godot UI traps in MobileClient

## A Control child of a rectless Control comes out zero by zero
Tags: gotchas, lessons | SetAnchorsPreset inside a parent Control that has no rect gives a 0x0 node that never draws - size and position it by hand instead

Every full-screen panel lives under a `CanvasLayer` whose `Control`
parent has no rect of its own. An anchored child of one of those is
0x0 and never draws, with no error. `LostConnection.Layout()` carries the
comment; every panel sizes itself from `GetViewportRect()` instead.

See also: the panels -> mobile-client.md

## Sibling draw order is build order
Tags: gotchas, lessons | Godot draws siblings in tree order, so a panel opened second can appear behind one opened first; Panels.ToFront moves it to last child

The reference client calls `moveToFront` whenever a window becomes
visible (`UIBuy.cpp:66`, `UITrade.cpp:80`, `UIObjectContents.cpp:61`). On
a desktop that is a nicety. Here every panel covers most of the screen,
so without it the button works, the panel is visible, and nothing appears
to happen. `Panels.ToFront(this)` in every `Open()`.

See also: Panels.cs | the reference -> ../../Meridian59.Ogre.Client/

## Subscribe, do not poll
Tags: lessons, architecture | A signature-based poll cannot see field-only mutations; BaseList re-raises an item's PropertyChanged as ListChangedType.ItemChanged

`SetToItem`, `SetToSpell` and `SetToSkill` set type and name as plain
fields, so a poll that hashes the visible properties never notices. The
list itself tells you: `BaseList` re-raises the item's `PropertyChanged`
as `ListChangedType.ItemChanged`.

See also: ActionButtons.cs

## A polled signature must hold everything the panel draws
Tags: lessons, gotchas | Where a panel polls instead of subscribing, every drawn field goes in the signature - including whether the name and the sprite have RESOLVED yet - and a failed compose is never cached

The library resolves strings and art after the numbers arrive, and raises
PropertyChanged for each, which the reference reacts to. A poll that
hashes only what was there on arrival never notices the later fill-in
and the row stays as first drawn, for the session. The rule has three
halves:

- The signature carries the resolution state: name (`ResourceName`),
  icon name (`ResourceIconName`) and whether the resource is there
  (`Resource?.Filename`, or `Resource != null`).
- A compose that comes back null is not cached (`if (tex != null)
  _icons[key] = tex`, `SpellsPanel.cs:413`, `AvatarPanel.cs:244`,
  `RoomBuffsPanel.cs:176-182`), or the miss is answered from the cache for
  ever for everything sharing that art.
- A sprite that exists but is not yet readable changes nothing in the
  data, so no signature can see it: retry on a 500ms timer while any
  compose failed (`SpellsPanel.cs:232-236`, `AvatarPanel.cs:157-161`).

Panels caught by it: `Vitals.cs:99-114` (a bar that arrived nameless
stayed nameless, and `ValueRenderMin` was missing), `RoomBuffsPanel.cs:78-95`
(room enchantment invisible for as long as the room held it),
`AvatarPanel.BuffIcon` (a buff that missed once was missing for the
session) and `SpellsPanel.cs:200-236` (a row stuck "(unnamed)" with no
icon). `NpcQuestsPanel.cs:281-282` was the same omission on another axis:
titles only, so a re-offered list with new text never rebuilt.

A fixture that shows it needs ONE row: with two, the sort order moves when
the names resolve, the signature changes for the wrong reason, and the
bug hides.

See also: the fixture -> fake-server.md | ActionButtons.cs

## Subscriptions attach after Init
Tags: gotchas | RootClient.Init() creates Data, so anything subscribing to Data must run after _client.Init()

See also: GameView.cs

## A ScrollContainer sizes its child to that child's minimum
Tags: gotchas, lessons | The rows' container must ask to expand, or the list is only as wide as its longest line and every column after the name lands where that row's text ended

Four panels had `SizeFlagsHorizontal = ExpandFill` on the NAME LABEL
inside each row, which is correct and does nothing on its own: the
VBoxContainer holding the rows is the ScrollContainer's child, and
without the same flag on IT the whole list stays at its minimum width.

What that looks like is not an obvious layout bug. Nothing overlaps and
nothing is cut off. The percentages and the bind buttons simply sit
wherever each row's text happened to end - a ragged column of tap
targets a thumb has to hunt for, with most of the panel going spare
beside it. InventoryPanel had the flag all along, which is why its grid
looked right and the lists did not.

See also: the panels -> mobile-client.md

## Landscape, and what had to move for it
Tags: design, architecture | The client is sensor-landscape now; three things were sized for a tall screen and had to be told the difference

`project.godot`: 1920x1080 and `handheld/orientation=4`, which is
sensor landscape - either way up, so the phone can be held with the
charger port on whichever side.

Three things assumed height:

- The render buffer was sized by its WIDTH, so a sideways screen left
  221 rows to draw a world in. It is sized by height now (432), and the
  width follows the screen: 936x432 at 21:9, which is the same pixel
  count the portrait client drew.
- The field of view was horizontal, so a wider screen kept the same
  swath and threw away the sky and the floor instead - 39 degrees
  vertical at 21:9, with a duskrat two metres away filling the frame.
  `Renderer.Projection` takes whichever of the vertical field of view
  and a horizontal floor is binding, so the view widens with the
  screen and a tall window still shows a room. The vertical angle is
  45 degrees, which is the game's: `OgreClient.cpp:126` creates the
  camera and never calls setFOVy, so it keeps Ogre's default. Sixty
  was a number of mine and Ashton saw it at once - every room looked
  bigger than it is, because a wider angle puts the walls further
  away.
- The chat, the target row and the hotbar stacked bottom-upwards, which
  put the hotbar across the middle of the screen - exactly where both
  thumbs drag. Sideways the chat and the target row keep to the left
  52%, and the hotbar takes the right and drops to just above the menu
  row.

`Panels.Side` is the shared margin: portrait returns what each panel
computed for itself, landscape adds enough to keep the content in a
centred band. A row with a name at one end and a number at the other,
stretched over two thousand pixels, cannot be read in one glance.

See also: the client -> mobile-client.md | Renderer.cs | Panels.cs

## The interface keeps off the glass's edge
Tags: design, gotchas | SafeArea insets the whole UI layer - the system's cutouts plus a flat margin for the curve, which nothing reports

Two problems, one answer. Android reports its cutouts - camera hole,
status bar, gesture bar - through `DisplayServer.GetDisplaySafeArea()`.
Nothing reports the curve of the glass, so `CurveFraction` adds 2.5% of
the screen's SHORT side on every edge, which is about the ten to
fifteen dp a rounded corner eats.

The whole `_ui` CanvasLayer is scaled and offset rather than every
panel being taught about insets: a panel lays itself out against the
viewport as it always has, and the layer it sits on is what lives
inside the safe rectangle. The world underneath is NOT inset - the
game should fill the glass. Taps still land where they look, which was
checked rather than assumed: a tap at the Map button's new coordinates
hides the map.

Two traps, both hit:

- Setting `layer.Scale` replaces whatever the engine put there. Here
  that is the identity, because this project's viewport rect equals the
  window - but a project where the stretch lives in the layer transform
  would have to multiply, not assign.
- `GetDisplaySafeArea()` off a handheld answers with the DISPLAY's
  size, not the window's. Under xvfb that is 1280x1024 against a
  2340x1080 window, which reads as a thousand pixels of cutout and
  shrinks the interface into a corner - which is what the first run
  looked like. It is only asked on Android and iOS now, and any inset
  over a fifth of the screen is refused whatever the answer.

See also: SafeArea.cs | the client -> mobile-client.md
