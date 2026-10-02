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

Two later cases of the same omission: `InventoryPanel`'s signature
lacked `IsApplyable`, so a flags-only change never reached `Sync`
(`InventoryPanel.cs:345-346` carries it and `IsInUse` now); and the
button caption was refreshed only when the surviving object was a new
INSTANCE, though the library flips `IsInUse` on the same one
(`DataController.cs:2481-2506`) - see "A model object held across a
rebuild goes stale" below.

A fixture that shows it needs ONE row: with two, the sort order moves when
the names resolve, the signature changes for the wrong reason, and the
bug hides.

See also: the fixture -> fake-server.md | ActionButtons.cs

## A model object held across a rebuild goes stale
Tags: lessons, gotchas | Hold the ID and look it up live; where the object itself must be kept, re-resolve it by ID on every Sync - four panels shipped lying about a thing the server had already replaced

The panels rebuild from the model, and the library either mutates an
object in place (`IsInUse`, name and flags, a stack's count) or replaces
it (`InventoryRemove` then `InventoryAdd` for a new stack). A reference
kept from the last build is wrong in both cases. Caught in one day:

- Loot: `_ticked` held the object instances, so a tick outlived its item
  and Get sent the counts they had when ticked, and `Close` never freed
  the rows, so a reopened empty list still drew live ghosts (6970025).
  It is a set of ids now, pruned on rebuild (`LootPanel.cs:108,307-311`),
  and Get walks the live model in list order (`:475-481`).
- Trade: `Put` stored the instance, so a replaced stack went on being
  offered as id 8003 at 25 after the server had made it 8004 x5. Put
  compares by ID (7b79a6c), and `Reconcile` (`TradePanel.cs:402`) runs
  every `Sync` and again at the Offer press: drops what the pack no
  longer holds, swaps in the live instance, lowers a chosen amount the
  stack cannot cover, and says so in-page. It stops once the server has
  echoed the offer, when `ItemsYou` holds the server's objects
  (`IsItemsYouSet`).
- Inventory caption: `_picked` is re-pointed at the live object and the
  button relabelled on every rebuild, not only on a new instance
  (`InventoryPanel.cs:366-391`); it drops the pick when the id is gone.
  The bag was right first and is the pattern.
- Amounts follow the same rule: a number the player chose lives in the
  panel keyed by id (`TradePanel._amounts`, read through `Chosen`), and
  the ceiling is the LIVE count - re-opening the prompt with `o.Count`
  as both value and ceiling had silently put the whole stack back
  (89aca73).

The reference gets away with holding instances because its rows are
destroyed by the list's own `ItemRemove` as the model changes
(`UITrade.cpp:480-481`, `UILootList.cpp:53-59`). Here the rows are
redrawn, so nothing does it for you.

See also: the harness fixture M59_STACK=shrink|replace -> fake-server.md | InventoryPanel.cs

## Free a row before you add its replacement
Tags: gotchas, lessons | QueueFree is deferred, so the old child still holds its name when the new one arrives and Godot renames the new one `@Button@NNN` - the panel works and cannot be scripted

`RemoveChild` then `QueueFree` (`CharacterPicker.cs:206-211`). The rows
still worked, matched by text, which is why nobody saw it; but
`@name:newCharacter` found nothing on every visit after the first, so
Cancel-then-retry had never been exercised.

See also: harness.md | CharacterPicker.cs

## Nothing may end up above an armed ConfirmPopup
Tags: design, gotchas | The reference's popup is AlwaysOnTop and not modal, so a question that is waiting for an answer is never covered by another window - here that is an invariant on tree order, held by whoever moves last

Both the popup's root and its window set `AlwaysOnTop`
(`Resources/ui/layouts/Meridian59.layout:2859,2873`); neither is modal.
In this tree "on top" is the last child, so a window that raises itself
has to put an open popup back above it (`GuildPanel.KeepPopupOnTop`,
`GuildPanel.cs:368`; `ConfirmPopup.cs:209-210` raises itself on show).
Raise only on the OPEN edge: `GuildPanel` called `Show(true)` on every
roster rebuild, so a server resend put the roster over "exile Boris?",
which stayed hidden and still armed.

The invariant is what to keep, not the state of the tree: check
whoever opens a panel while a popup can be up (`GuildAsk` opening
`GuildCreatePanel` was an open case at fa28d39). `ConfirmPopup.Choice`
and `Tell` silently replace a pending choice - not reference behaviour
to copy, and unreachable once touch cannot start a second choice.

See also: Panels.cs | ConfirmPopup.cs

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

## An editor's own chrome is a piece too

The HUD editor's bar started as one strip across the top of the
screen. Every piece under it became undraggable, and on a full HUD
that is not a corner case: the Menu/Map band is pinned to the top
centre by design, so the one group a player most wants moved was the
one group they could not touch. Moving the bar does not fix it -
there is no strip of a full HUD that belongs to nobody. Two answers,
both needed:

- The bar is two plates, left and right, with the centre of the
  strip left clear.
- The bar FOLDS. One button takes it down to a pill and gives the
  whole glass back, which is the only answer that works for a piece
  dragged under a plate.

And the trap that followed: `Dodge` took its floor from the bar's
rectangle. Folded, that rectangle is the pill at the BOTTOM, so
every candidate was below the bottom edge, none passed the floor
test, and the card fell back to a default that was half off screen.
A candidate that fails a bound is clamped into the screen, never
skipped - a filter that can reject every candidate has no answer at
all for the case it rejects.

See also: HudEditor.cs, M59Hud.cs | the HUD store -> mobile-client.md

## A world overlay does not belong on the interface's layer

Name tags and quest marks are placed at a point the renderer
projects - the world's own screen coordinates. They were children of
the UI CanvasLayer, and SafeArea scales and shifts that layer so the
interface stays off a phone's cutouts and curve. The world
underneath is deliberately NOT inset.

So every name was drawn at `p * s + offset` while the head it names
was still at `p`. At 1920x1080 with the 2.5% curve margin that is
s = 0.95, offset = 48: exactly zero error at the centre of the
screen, growing to 40 points at either edge and in OPPOSITE
directions, so names are pulled toward the middle. The player in
front of you looks right and everybody else looks wrong, which is
why it reads as "not centred" rather than as an offset.

It survived every screenshot because the error is zero where the
harness usually looks, and because a Control's own Position - which
is what a @state dump prints - is in LAYER coordinates and does not
include the layer's transform. The dump said 960 and the screen
said 912.

Both now live on their own CanvasLayer with no inset, under the
interface and over the world. The reference never has this problem:
there the name is a billboard in the 3D scene
(`RemoteNode::UpdateNamePosition`), so it is in the world by
construction.

See also: SafeArea.cs, NameTags.cs, QuestMarkers.cs

## A ScrollContainer child ignores CustomMinimumSize when it expands
Tags: gotchas, lessons | ExpandFill takes the container's whole width, so the minimum is a floor the layout never reaches; and ContentMargin on a scrollbar stylebox insets nothing

Two failed fixes for the same complaint, both of which looked right in
the code and did nothing in the frame.

Every list's row box is ExpandFill - it has to be, or a VBox shrinks to
its longest line and every value column lands wherever that row's text
ended. ScrollContainer fits its child with the child's own size flags,
so an expanding child takes the full width and draws under the bar.
`CustomMinimumSize` is therefore a FLOOR, not a width: raising it moved
nothing. The fix is `SizeFlags.ShrinkBegin` plus an exact minimum, which
is what M59Skin.RowsFit sets - both calls together, because the one that
is forgotten is the one that undoes the other.

Separately: `StyleBoxFlat.ContentMargin*` tells a box where its CHILD
content goes. A ScrollBar has no child, so setting it changed nothing -
the gold ran the bar's full width. `ExpandMargin*`, negative, is what
shrinks the box that actually gets DRAWN. M59Skin.BarInset uses it to
leave a 30-point dead margin on the bar's left that still takes the
press.

Both were only visible in a screenshot. Neither produced a warning.

See also: the skin -> ../M59Skin.cs
