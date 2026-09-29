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

## Subscriptions attach after Init
Tags: gotchas | RootClient.Init() creates Data, so anything subscribing to Data must run after _client.Init()

See also: GameView.cs
