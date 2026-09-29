# MobileClient

Godot 4 (.NET) front end for Meridian 59, built on the ported
`Meridian59` core library. Android and iOS are the targets; the desktop
editor is just where it gets developed.

## Where this stands

Landed on `net8-core` (2026-09-28). What runs, what is checked, and what
is still unknown - so the next person does not have to re-derive it.

**Runs.** The Godot client draws the world, and its output is
pixel-identical to the offline renderer. The whole live path - login,
character select, room entry, the object list, movement, chat, targeting,
looting, looking - runs end to end against
`Tools/Meridian59.Net8FakeServer` on loopback. Every widget has been
photographed doing its job; `SceneShot.tscn` and `UiShot.tscn` take those
pictures, and they can inject a touch and press a named button, so
tap-to-target is exercised too.

**Checked against the library rather than by eye.** 362 rooms render
byte-identical threaded and single-threaded; 8357 wall UVs and 604,901
flat UVs agree with the library's own arithmetic; 134,887 slope rays
agree with a bisection; 8.3M pixels of tap-picking agree with what was
drawn; 404 objects compose to exactly the picture the single-frame path
drew. `Tools/Meridian59.Net8RenderCheck`, `Net8Uv`, `Net8Units`,
`Net8World`, `Net8Compose` and `Net8Verify` are those checks.

**Not yet run against the real server.** Everything above is the fake
one, which answers the protocol but invents the world. In particular
`BaseClient.SendLoginMessage` sends `"8"` as the RSB hash where a local
change on the author's machine used `"13"`; if the real server checks it,
this branch cannot log in until that is settled.

**Known gaps, in rough order of how much they matter:**

- Nine art files cannot be decoded at all - the CRUSH-compressed ones.
  The dragons, two beetles and `ranu` are invisible. Needs a Windows box
  to re-save them; see the note further down.
- Nothing has run on a phone. The Android export preset exists; a JDK,
  the Android SDK, export templates and a device do not.
- Per-sector light values are read but unused, which is what the
  reference client does too. See "Light is the room's ambient" below.
- `WF_BACKWARDS` direction, and which way scrolling water flows, both
  need somebody who knows what these rooms should look like.
- Inventory containers - the bag does not open them, and dropping an
  item onto a container object in the room (`SendReqPut`) is not wired
  up. Rearranging the bag itself is done; see below.
- Slope seams at flat-span boundaries.

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
it. A folder you typed on an earlier run wins over all of them.

If none of them has any `.roo` or `.bgf` in it, the view asks rather
than stopping: it lists the places it looked and gives you a box to type
where yours actually is. What you type is checked for real room and
bitmap files before it is accepted, and remembered in `user://` so the
question is asked once. The places we look are guesses - installs move,
and people keep them on other drives.

## Scenes

- `FirstPerson.tscn` (the main scene) - first-person textured view, no
  server needed. The **Rooms** button lists every `.roo` in the resource
  folder with a filter box, so all 362 rooms are walkable from inside
  the app rather than by editing the scene. This is the build worth
  sideloading first: Meridian on a phone with no account and nothing to
  connect to.
- `Game.tscn` - the live view: connects to the server and renders the
  room the avatar is in. Moves and turns are sent to the server.
- `Main.tscn` - top-down map: textured floors per BSP leaf, walls over
  the top. T toggles textures, W walls, F refits.

## Controls

Left half of the screen is a floating movement stick - it appears where
your thumb lands. Right half is look: drag to turn. Each tracks its own
finger, so moving and turning at once works, which is why the screen is
split rather than given fixed on-screen buttons.

Dragging up and down on the look half looks up and down. A column
renderer cannot rotate the camera about its own X axis without giving up
the thing that makes it fast - that every wall is vertical on screen -
so it shifts the horizon instead, the same trick Doom used. That is a
shear rather than a rotation, so it exaggerates the further you push it
and is clamped at about 31 degrees. At zero the renderer is unchanged
to the byte, which is what keeps the checks in `Tools/` comparable.
`Net8Fpv --pitch <radians>` renders it.

Tap - a finger down and up without moving - targets whatever you touched.
The renderer picks the sprite under that pixel, opaque texels only and
never through a wall, and that object becomes the library's own target,
which is what the Look / Get / Attack / Use row acts on. Tapping nothing
clears the target and hides the row.

Enter opens the chat line, Escape closes it. What you type goes through
the game's own command parser - `tell`, `cast`, `perform`, `rest`,
`guild`, `invite`, `group`, `deposit`, `appeal`, `time` and twenty more,
with the aliases from your config and the command history the library
keeps. Commands are word-based, as in `tell bob hi`; anything that is
not one is said.

**Map** in the bottom right corner toggles a map of the room with a
wedge showing where you are and which way you are pointing. Walking 362
rooms with no idea which way you came in is the main thing that makes
the offline build tiring. The walls are drawn once into a texture when
the room loads - some rooms have six thousand of them and redrawing
those every frame would cost more than the game does - so only the
marker moves.

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

  It runs on a worker thread with the count on screen, because that is
  hundreds of megabytes and doing it in `_Ready` would freeze the app
  long enough on first launch for Android to decide it had hung.

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

## Seeing the UI without a server

The widgets can be built, opened and photographed with made-up data, so a
layout bug does not have to wait for a live connection to show itself.
`UiShot.cs` and `UiShot.tscn` do it; nothing else loads that scene.

Godot's own build of the .NET packages is inside the engine download, so
no package feed is needed:

    # point the project at the unpacked engine's nupkgs
    cat > MobileClient/nuget.config <<'EOT'
    <?xml version="1.0" encoding="utf-8"?>
    <configuration>
      <packageSources>
        <clear />
        <add key="godot" value="/path/to/Godot_v4.7.2-stable_mono_linux_x86_64/GodotSharp/Tools/nupkgs" />
      </packageSources>
    </configuration>
    EOT

    dotnet build MobileClient

    xvfb-run -a /path/to/Godot_v4.7.2-stable_mono_linux.x86_64 \
      --path MobileClient --resolution 900x1600 UiShot.tscn -- \
      --out inventory.png --res /tmp/res --pick 0

`--headless` does **not** work for this: it loads the dummy renderer and
the shot comes out blank. A virtual display is enough - `xvfb-run` above
runs it on llvmpipe with no GPU. `--pick N` selects an item so the action
row shows.

The first runs of this found things that reading the code had not. The
panel ignores a `Sync` while it is closed, which is right for the live
client - `GameView` syncs every frame - and a trap for anything that
fills it once, so the harness opens first. The item buttons had
`ExpandIcon` set, which hands the icon whatever width the label leaves
it: an ear of corn is 860 pixels across and came out a sliver three
pixels wide, while a 17-pixel ankh towered over everything. And the dye
bottle's icon vanished entirely, because its composed origin comes out at
-0.0000019 and the composer refuses a negative origin - rounding first,
the way the library converts these numbers everywhere else, turns that
hair into the zero it plainly is.

## Running the live view without a server

The live half - login, character select, room entry, the object list,
chat - was written from reading the library and had never been run, since
the real server is not reachable from this machine.
`Tools/Meridian59.Net8FakeServer` answers enough of the protocol to get a
real client into a room, on loopback, with no account:

    dotnet run -c Release --project Tools/Meridian59.Net8FakeServer -- 15999 /tmp/res barinn.roo

    M59USER=tester M59PASS=x xvfb-run -a <godot> --path MobileClient \
      --resolution 900x1600 SceneShot.tscn -- \
      --out live.png --res /tmp/res --host 127.0.0.1 --port 15999 --wait 300

It reached the world on the first honest attempt and then found things:

- `Tools/Meridian59.Net8Play` called `Init()` before setting
  `ResourcesPath`, which is the same ordering bug `GameView` had - `Init`
  reads that path, so setting it afterwards means `Init` ran against
  nothing. It crashed on a null path the moment there was a server to
  connect to.
- The first objects the server sent stood a few units from the camera and
  filled the screen with one duskrat. Those are the server's own units:
  one of them is sixteen room units, and a grid square is 64 of them.

See that tool's README for what the handshake actually needs - in short,
`GameState` is the hinge that moves the client's parser out of login
mode, the client verifies no CRC on what it receives, and chat on the
wire is a string resource id rather than text.

## Enchantments and the target

Two smaller mirrors from the same pass.

The avatar panel carries a grid of **enchantment icons** under the
portrait - whatever is in the client's own `AvatarBuffs`, each composed
the way the game composes a buff icon: front frame, no Y offset, centred.
Sixteen pixels there, bigger here, because a phone is not a mouse
pointer.

The **target row** shows its name in the colour the server gives that
object, through the same `NameColors.GetColorFor` the loot list, the look
window and the labels over people's heads use - and, like the game's
target window, it hides itself when the target has no name or is flagged
invisible. It used to be gold for everything.

## Spells and skills

The game has two windows of the same shape - `UISpells.cpp` and
`UISkills.cpp` - each a list whose rows are an icon, the name, and how far
along you are with it as a percentage, with a double click to cast or
perform.

Two lists is one panel with a pair of tabs here, because a phone has room
for one and the rows are identical. That is the only departure.

Worth knowing: a spell arrives as **two** things and the client needs
both. `SpellsMessage` carries the objects, which is what a cast is
resolved against - `SendReqCastMessage(uint)` looks the id up in the
client's own list - while the list the window shows is a *stat group*,
`StatGroup.Spells`, whose rows carry the name resource and the
percentage. Sending only one of the two gives either an empty window or a
window that cannot cast.

## Looking at something

`UIObjectDetails.cpp` is a window with four things in it: a picture of
the thing, its name in the colour the server gives it, the description
the server sent, and an inscription underneath when it carries one. This
client could send a look and had nowhere to put the answer.

None of it is the view's decision. `Data.LookObject` is an `ObjectInfo`
the library fills from the reply - the object, the message, the
inscription, the look type, and whether the window is up - and the panel
follows it.

One difference from the inventory's icons, and it is deliberate in the
game: the picture is composed with the **viewer's** frame rather than the
front one, so a creature in the look window faces the way it faces in the
world.

A protocol trap found building the fake server's reply: `ObjectInfo`
reads its object back with `new ObjectBase(...)`, so the object in a look
must be an `ObjectBase`. Writing a `RoomObject` - which also carries a
position, an angle and a motion animation - leaves the reader mid-object,
and everything after it is garbage: the description came back empty with
a resource id in the billions.

## Action buttons

The game keeps a grid of them - `UIActionButtons.cpp`, twelve across and
four down - filled from the player's own configuration: spells, skills,
items, commands and aliases. This client had nothing of the sort. The row
it already had is a different thing, the actions you can take on whatever
you tapped.

What a button *does* is not decided in the view. Each is an
`ActionButtonConfig` in the client's own list, and calling `Activate()` on
it is what fires it: `BaseClient` is subscribed to every button and
dispatches by type - a spell casts, a skill performs, an item is used,
applied or unused, an action runs, an alias runs as a chat command.
Writing that dispatch again here would be a second copy of five rules.

Item buttons bind themselves. The data controller matches a button by
name against the inventory as it arrives, so a button named for something
you are carrying becomes that item, icon and all.

Two departures, both marked in the code. Twelve by four does not fit a
phone, so this shows one row of however many fit across. And the starting
set is this client's own: the game ships no default buttons at all - its
list comes from a configuration file written by a UI this client does not
have - and an empty row on a phone is a row of nothing. The defaults are
the library's own avatar actions.

A trap worth recording: `ActionButtonConfig.Label` is an empty string when
unset rather than null, so `Label ?? Name` picks the blank one and every
button reads "?".

## The loot list

The game has a loot window - `UILootList.cpp` - and it is a list rather
than a grid: one row per item, each an icon, the item's name and how many
there are, with Get for the one you picked and Get All underneath. This
client had the Get All button and no way to see what you were about to
take.

The name's colour is `NameColors.GetColorFor(flags)`, which is the
library's own function and is called rather than copied. In vanilla that
is white normally, orange for an outlaw, red for a killer, yellow for a
creator, green for a super-DM, cyan for a DM, purple for an event
character, and black for anything flagged to draw black.

The window is the server's decision, not the view's:
`ObjectContents.IsVisible` goes up when the server sends the contents of
something and down when it takes them away, so the panel follows that
rather than a button. The fake server sends a pile on room entry, since
there is nothing here to open.

## Chat is styled, not tinted

The server does not send a coloured line. It sends text plus a list of
**styles** - each a start, a length, a colour and whether that run is
bold, italic, underlined or struck - and the Ogre client's
`Util::GetChatString` walks that list to build its markup. This client
used to throw the list away and tint the whole message by its kind.

The styles come out of inline markers in the string itself: `~B` for
bold, `~n` back to normal, and a colour letter for one of vanilla's six.
`ChatStyle.GetStyles` parses them and `RemoveInlineStyles` takes them back
out of the text, so the view never sees the markers - only runs.

The colours are not the obvious ones: chat red is `0x800000` and chat
green `0x006400`, both dark, purple is `0x8F26AA`. Those six are all
vanilla has; this server's flavour has thirty-odd more, and a client that
knows only six draws the rest as white. The default colour of a message
depends on its kind - a server message starts purple, a system message
blue, someone talking white.

They are dark because the game puts them in a chat window with a
background. Over a bright floor, dark red on red is hard to read; the
lines carry an outline here for that reason, which the game does not need.

## The condition bars are the game's condition bars

`UIAvatar::ConditionChange` in the Ogre client is the spec, and it
differs from a health bar in four ways worth knowing:

- there is **one bar per entry in the avatar's condition list**, in the
  server's order - not three hardcoded stats. Vanilla sends a fourth, the
  chance of getting tougher, which this client used to throw away.
- the fill is **not** current over maximum. It is
  `(current - renderMin) / (max - renderMin)`, and `max` is the *render*
  maximum for vigor and tougher-chance but the plain maximum for
  everything else. A stat whose render floor is not zero reads wrong the
  other way.
- the colours are the client's own, and darker than a health bar usually
  is because the bar imagery lightens them: hit points `0x800000`, mana
  `0x000080`, vigor `0x707000`, tougher-chance `0x444444`.
- below a third it blinks, and keeps blinking while it stays there. The
  game starts a one-shot highlight on every change and switches that
  animation to looping under 33%.

The bars sit in the top-left corner with the portrait to their left,
which is how the game arranges its avatar panel: the head at 13,14 and
the condition bars starting at x=95 of a 250-wide panel. The status lines
moved down to make room.

The fake server sends a deliberately low vigor so the blink has something
to do.

## The portrait is the object composed from its head

The game's avatar panel shows your face, and it is not separate art: it
is your own object composed **from its HEAD hotspot downwards**, with the
front frame, no Y offset, centred in the box. `UIAvatar` passes exactly
those arguments to its composer, and `RenderInfo` picks the suboverlay
hanging on that hotspot as the compose root.

`M59Compose.Icon` takes the hotspot for this, and
`Tools/Meridian59.Net8Compose portrait` shows the difference side by
side: the whole object, then the same object composed from hotspot 1.
The art in this container has no player bodies - nothing carries a head
hotspot with a part on it - so that demonstration builds the case by hand
from a flagpole and an ankh, and the panel in the live view falls back to
the whole object, which is what `RenderInfo` does when the hotspot is not
there.

## Which Meridian this is

The library carries two implementations of several things - object flags,
minimap colours, name colours, the chat palette - switched by a `VANILLA`
define, and **nothing in this repo defines it**, in any configuration. So
the build is the non-vanilla one, and the `#else` branches are dead code
here.

That matters because it decides how the client reads what the server
sends, and the first widgets mirrored here were mirrored off the wrong
branch. Server 104's own `include/proto.h` settles it:

- `OF_DISPLAY_NAME 0x00000001` exists as a flag, and the name is drawn on
  that rather than worked out from a player type
- the minimap dot is its **own bitfield** - `MM_PLAYER`, `MM_ENEMY`,
  `MM_MONSTER`, `MM_NPC`, `MM_MINION_SELF` and the rest - separate from
  the object flags, where vanilla folded it in
- the name colour is sent as **hex RGB**, `NC_OUTLAW 0xFC9E00` and so on,
  rather than derived from a type
- drawing effects are their own field too

The wire format agrees: `ObjectFlags.WriteTo` in this build sends drawing,
minimap, namecolor, player and moveon as separate fields after the base
flags, which is what that server writes.

So this client follows the non-vanilla branches throughout: the minimap
dots and rings, the name-display rule, and the full chat palette of
thirty-odd colours rather than six. A client mirroring the vanilla
branches would read flags this server never sets - no names on signs, the
wrong minimap dots, and two dozen chat colours drawn as white.

## The minimap is the game's minimap

The first version drew the whole room squeezed into a box, all walls, a
gold wedge for you. The game does something quite different, and both
`Meridian59/Drawing2D/MiniMap.cs` and the Ogre client's `MiniMapCEGUI`
say what:

- it is a **window onto the room centred on you**, at a zoom, not the
  whole room. Half the view's width and height times the zoom is how far
  it reaches, so zooming out shows more room at the same size. The
  library's default zoom is 4; the wheel in the Ogre client moves between
  1 and 32.
- a wall with no sides at all, or whose every side is flagged
  `IsMapNever`, is **not drawn** - that flag is how the game hides scenery
  it does not want you navigating by.
- objects are dots, coloured from the server's own `MM_*` minimap
  bitfield in `MiniMapCEGUI::DrawObject`'s order, with an outer ring
  saying what a player is to you - builder group, friend, enemy,
  guildmate - or, for anything else, whether it has aggro or a quest. An
  object with no minimap bit gets nothing, which is most things on a
  floor. Invisible objects are skipped.
- you are a **triangle**, in the player colour, built from your facing
  direction and two copies of it rotated by half a turn less half a
  radian - a wide arrowhead rather than a needle.
- it is **round, on a wooden dial**. The CEGUI layout puts
  `TaharezLook/MiniMapBackground` behind the map and draws the map over it
  at nine tenths alpha. That art is in this repo, in
  `Resources/ui/imagesets/TaharezLook.png` at 257,256 - it is cut out of
  there into `MobileClient/art/minimap-bg.png` rather than drawn from
  scratch.

Coordinates are the server's own, because that is what the library's
minimap works in: a wall vertex becomes `X * 0.0625 + 64`, which is room
units over 16 offset by 64. Both views convert before handing anything
over.

One deliberate difference: walls are cut to a circle inside the rim. The
game's map texture fills its square window, so its walls run under the
frame; it only uses a circle for hit-testing. A map that stops at the
frame looks like a map rather than a leak.

## The inventory is the game's inventory

The first version of this panel was a list of labelled buttons, which is
not what Meridian 59 has. `Meridian59.Ogre.Client/UIInventory.cpp` and
`Resources/ui/layouts/Meridian59.layout` say what it is, so the panel now
follows them rather than my idea of a bag:

- five columns (`UI_INVENTORY_COLS`), six rows to start
  (`UI_INVENTORY_MIN_ROWS`), rows added as the bag fills and removed as it
  empties, never below the minimum
- every slot drawn whether or not it holds anything
- the icon 40 pixels square (`UI_INVENTORYICON_WIDTH`) in a slot of 52
- the item's **count** printed on the icon, no labels
- the name belongs to the selected item - a tooltip there, the line above
  the buttons here
- an item in use is marked; the game turns the composer's glowing
  background on for those

Icons are composed differently from world objects, and that is also from
`UIInventory.cpp` rather than guessed: the **front** frame rather than the
viewer's, `ApplyYOffset` off, no power-of-two padding, scaled into the
icon box and centred both ways. Composing them the world way gives icons
that face wherever you happen to be standing and sit at the top of a tall
empty box.

Two things are deliberately not the game's. The slots grow to fill a
phone's width instead of holding the game's fixed 52 pixels in a window
284 wide - five across either way. And a tap selects while a second tap
uses, where the game targets on a left click and uses on a left double
click.

Dragging one item onto another rearranges the bag, as it does there.
`Inventory::OnItemDropped` takes the item out of the client's own
inventory list and reinserts it at the other one's position, and only
then sends `SendReqInventoryMoveMessage(from, to)` - so the bag settles
under the finger rather than after a round trip, and the two ids in the
message are object ids, because each slot's window id is set to the
object's id. Dropping onto an empty slot is ignored there, because the
index falls outside the data list, so it is ignored here.

The slot is its own control (`InventorySlot.cs`) and handles its own
tap. A Button laid over the top swallows the press, and then nothing is
ever a drag.

`UiShot.tscn --drag i,j` runs it with real mouse events, which is the
only way to make Godot's drag and drop happen:

```
[UiShot] order was: long sword, nerudite axe, cookie, ear of corn, ...
[UiShot] dragged slot 0 onto 2
[UiShot] moved long sword onto cookie
[UiShot] order now: nerudite axe, cookie, long sword, ear of corn, ...
```

## Sound is the server's, mixed by distance

Nothing in the library plays anything. It reads the sound messages only
to notice an "ouch" and set a health status from it, because playing is
the engine's job - which is why the Ogre client hooks the message
stream in `ControllerSound.cpp` rather than getting sound handed to it.
`M59Sound.cs` does the same job here and follows that file:

- `PlayWave` with a source object id plays at that object,
- otherwise, one with a row and column plays at the middle of that grid
  square, `(column - 1) * 1024 + 512` in room units, converted to the
  server's own units the same way the game converts it
  (`* 0.0625 + 64`),
- otherwise it plays at you,
- `StopWave` stops a looping one,
- `PlayMusic` sets the room's background track, looped, and changing it
  only when the track actually changes so walking around does not
  restart it.

The listener is `AvatarObject.Position3D` and its angle, exactly as the
game sets it, so the 2000-unit maximum distance is in the same units the
game measures it in. There is no 3D scene here - the world is a
raycaster drawing into a texture - so the mixing is approximated:
volume falls off linearly to that same limit, and the sound is panned
by the component of the direction across your facing, which is what a
listener orientation would have given.

Three things the library makes you handle:

- The files are Ogg Vorbis on disk although everything calls them wavs.
  `PlaySound` changes the extension itself; **`StopSound` does not**, so
  a stop never matches the loop it is meant to stop unless both names
  are normalised first. `M59Sound.Key` does that.
- `PlayMusic` and `StopSound` arrive as string-resource ids like
  everything else, and nothing resolves them for you -
  `ResolveResources` has to be called before the name or path exists.
  Skipping it gives an empty name and a silence that looks like a
  missing file.
- Looping is a property of the stream in Godot, not of the player, and
  the streams are cached per file - so a looping sound is given its own
  copy rather than making every later one-shot of the same file loop.

`Tools/Meridian59.Net8FakeServer` sends all four on entering the room, so
the path can be run end to end offline:

```
M59SOUNDLOG=1 godot --path MobileClient SceneShot.tscn -- \
    --host 127.0.0.1 --port 15999 --res /tmp/res --wait 120
```

```
[M59Sound] Rat_awr.ogg gain 0.68 pan 0.00 loop False
[M59Sound] music AMBCave.ogg
[M59Sound] Rat_awr.ogg gain 0.67 pan -0.80 loop True
[M59Sound] stop asked for 'rat_awr.ogg', loops 1
[M59Sound] stopped rat_awr.ogg
```

That is a container with no audio device, so Godot falls back to its
dummy driver: what is proved there is the resolution, placement,
fall-off, panning and loop bookkeeping, not that a speaker moved. The
sounds themselves have not been heard yet.

## Light is the room's ambient, and nothing else

The reference client does exactly one thing with light.
`ControllerRoom::AdjustAmbientLight` takes the larger of the room's
`AmbientLight` and the avatar's own light - night vision, a lamp - and
sets that as the scene's ambient, as a plain ratio of 255
(`Util::LightIntensityToOgreRGB`). That is the whole of it.

`RooSector` does carry a light value, and the library works two numbers
out of it: sectors 128-255 are scaled by the room's ambient
(`AmbientLightModifier`, 0 to 2), sectors 0-127 light themselves
(`OwnLight`, 0 to 1). Nothing in this repo reads either one. Rather than
invent a way to combine them, the renderer does what the client does: a
`Brightness` of 0 to 1 multiplies the distance shading, so a dark room
is dark everywhere.

Both values come from the server and both change while you play - dusk
falls, a spell wears off - so `GameView` reads them every frame rather
than once on entering the room. A room that has said nothing yet is left
at full brightness, because rendering it black would look like a broken
renderer rather than an unlit cellar.

Measured against the fake server, mean pixel value over the whole frame:

| room ambient | mean |
|---|---|
| 255 | 60.45 |
| 160 | 38.92 |

which is the 160/255 the formula asks for, less the overlays, which are
UI and not lit.

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
  walks the BSP tree. Its third argument is the mover's current
  *elevation*, not a body height - the library passes `Start.Y` there in
  its own `VerifyMove` - and this passed zero, which tells the step-up
  and fall checks you are standing at world height zero. In a room whose
  floor is at three thousand that is a long way underground, and it was
  refusing moves for it: across 19200 attempts, 4090 that were blocked
  are allowed once the real elevation goes in, and **none** that were
  allowed became blocked. Being strictly more permissive in one
  direction only is what you would expect from removing a false block
  rather than loosening the collision. Verified across all 362 rooms: 4320 of 4320
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
- **Object size** comes from the art, not a constant. The library sizes
  an object as its frame's pixels over the file's shrink factor
  (`RenderInfo`), and one of those units is 16 world units - the same
  relation the wall textures give, where size over shrink of 64 spans a
  1024-unit grid square. With the fixed height this used before, a
  duskrat and a cyclops were the same size.

  Measured: a duskrat is 0.57 grid squares tall, a Knight 0.88, a
  cyclops 3.92. The corroboration for the constant is that a Knight's
  eyes then land at 68% of its height, which is about where eyes go.
  Setting `Sprite.Height` above zero still overrides it, which is what
  the checks in `Tools/` do so their numbers stay comparable.

  The frames' `YOffset` **is** applied now, by composing the object the
  way the game composes it rather than by nudging a frame-sized picture.
  It was left out before because the values looked implausible for a
  straight vertical placement - duskrat -11, Knight +30, cyclops +335,
  which over shrink and into world units is more than the cyclops's own
  height. Reading `RenderInfo` says why: the offset is not a nudge, it
  is part of the picture's box. An arrow's art carries a Y offset of
  -200 at shrink 5, and the composed picture is 208 pixels tall with the
  art in the top 8 rows and 200 rows of nothing below it. Anchored at the
  bottom like everything else, that lifts the arrow some 640 world units
  off the floor, which is what the offset was always for. The picture in
  `Tools/Meridian59.Net8Compose`'s `sheet` mode shows it: the arrow is a
  fleck at the top of a tall empty box. Measured over the 558 object files
  to hand, 142 carry an offset or are clamped by the quality cap, which
  is how many objects the single-frame path sized or placed wrongly.

- **Objects are composed**, not drawn as one frame. `M59Compose.cs`
  builds the picture out of the main overlay and its suboverlays: parts
  pinned to hotspots on the frame below them, each its own BGF. The
  clearest case in the art to hand is the first-person arm - `bri.bgf`
  and its siblings - which is a sleeve and a hand, and the `*ov` files,
  which are the weapons that pin into it: `neruaxeov`, `spirswordov`,
  `wandov`. Bodies and creatures are assembled the same way. Drawing the
  main frame alone dropped every part, and with it the per-part colour
  translation that dyes them.

  None of the arithmetic is guessed. The layout comes from the library's
  own `RenderInfo`, which is called rather than copied; the draw order
  and the per-part palette come from `ImageComposer`, which runs three
  passes of underlays, the main frame, then three passes of overlays;
  and the pixels are laid down by the library's own scaler, so a frame
  lands here exactly as it lands in the Ogre client. Pictures are cached
  on the object's `ViewerAppearanceHash`, which is what the library
  keys its own image cache on - it changes when the frame, the facing,
  the parts or their colours do, and not when the object merely moves.

  Checked by `Tools/Meridian59.Net8Compose`: 404 objects with no parts
  and no offsets compose to **exactly** the picture the old single-frame
  path drew, pixel for pixel, so composition did not quietly resize
  everything that was already right; a part hung on a hotspot lands
  inside the box the library gives for it; and in the overlap, an
  underlay changes nothing behind the main frame while an overlay
  repaints it. That last one is measured with a part pinned to a copy of
  the main frame's own art, purely because a weapon covers too few pixels
  of a hand for the difference between "behind" and "slightly left" to be
  countable - it is a test fixture, not anything the game composes.

  Two rounding bugs came out of that check, both from casting where the
  library converts. `Convert.ToInt32` rounds and a cast truncates, so a
  box of 478.00003 became 479 pixels wide with an empty column down one
  side, and a part 453.9998 wide lost its last column.

  **Hanging objects** are pinned by their top, not their base.
  `RemoteNode2D` picks `BBO_TOP_CENTER` for them, and excludes players
  in its vanilla build because the flag overlaps some player types on
  the original server - this does the same, so lamps and signs hang and
  no player dangles.

- **Floors and ceilings anchor per leaf**, not at the world origin, which
  is what `RooSubSector.UpdateVertexUV` does:

      uv.X = |vertex.Y - top| - (TextureY << 4)
      uv.Y = |vertex.X - left| - (TextureX << 4)
      uv *= 1/1024

  Two things worth keeping straight. The scale is a flat 1/1024 with no
  shrink and no texture size in it - floors do **not** scale the way walls
  do, and generalising the wall rule to them would have been wrong. And
  `left` and `top` start at zero and are only ever lowered by a vertex, so
  they are zero unless the leaf reaches into negative coordinates; that is
  why anchoring at the origin looked right nearly everywhere.

  Measured over the 362 rooms: 8573 leaves of 162787 anchor elsewhere, and
  5404 of those - 3.32%, in 188 rooms - move by a fraction of a texture,
  which is the only kind that shows. A whole texture's shift on a tiling
  texture is no shift at all. Standing on each of those leaves and
  rendering the room both ways, 44 rooms change: the valleys and sewers
  worst, up to 80% of the pixels in `dvalley3`.

  `FlatAnchors.cs` stores only the leaves that differ, in a grid over
  their own bounding box, so a room entirely in positive coordinates costs
  one comparison per pixel and nothing else. Even at the worst view in the
  worst room the cost is inside the noise - 3.91 ms against 3.84.

  Verified against the library's own numbers: 604,901 flat vertices in
  154,001 unsloped leaves, all agreeing, 28,981 of them on leaves anchored
  away from the origin; and the lookup by position - which is what the
  renderer actually does, rather than being handed the leaf - finds the
  right corner for all 7369 anchored leaves. `Tools/Meridian59.Net8Uv`
  checks the arithmetic, `Tools/Meridian59.Net8RenderCheck anchor` counts
  the pixels.

  Sloped leaves are built from three points on the plane instead of a
  corner and are left alone.

- **Nine files of art cannot be decoded at all.** Frames come in two
  compressions: one the library undoes with .NET's own inflate, and
  CRUSH, which it can only undo by calling a proprietary `crush32.dll`
  from an x86 Windows build. There is no source for that DLL in the
  repo, so those frames throw here and would throw on a phone.

  Counted over the 558 object files to hand: 201 frames, 2.93%, all of
  them in nine files - `edragon`, `idragon`, `rdragon`, `gbeetle`,
  `rbeetle`, `ranu` and their `X` variants. Every frame in each, so
  those creatures are simply invisible rather than patchy. The composer
  skips an undecodable part instead of losing the whole object, which
  matters for an object with one bad part but does nothing for a file
  that is bad throughout.

  Two ways out, neither doable from here: re-save the nine files with
  the original tool on x86 Windows, or rebuild them from the `.bmp`
  sources in the server's `resource/graphics`. Both need a Windows box.

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

  Health, mana and vigor sit above the chat as three bars, read from
  the client's own `AvatarCondition` stats rather than copied - the
  library keeps those current from the server, maxima included. They
  draw nothing until the server has sent something, so they stay out of
  the way while connecting, and only redraw when a number moves.

  A **Bag** button opens the inventory: what you are carrying, with the
  icon the library says to show for each object (`ViewerFrameIndex` on
  its own art), the count where several share a name, and a mark on
  anything in use. Tapping an item offers Use, Drop and Look - and the
  Use button says Apply or Unuse instead where that is what will
  happen, because `BaseClient.UseUnuseApply` decides between the three
  from the item's own flags rather than from a guess here.

  It reads the client's `InventoryObjects` rather than copying them, and
  rebuilds only when the list actually changes - a signature over id,
  count and in-use state - because an inventory is a few dozen buttons
  with a texture each. Opening it asks the server for a fresh list.
  Movement is suspended while it is open, the same as while typing.

  Drop uses `ObjectID(id)` with the default count, which is the
  convention the library's own `LootAll` uses for picking things up.
  Containers and moving things between them (`IsContainer`,
  `SendReqInventoryMoveMessage`) are not done.

  A **Loot** button in the bottom right calls the library's `LootAll`,
  which picks up everything gettable within close distance in one go.
  Tapping each item individually is exactly the sort of thing a phone is
  bad at.

  An account with more than one character gets a full-screen picker and
  nothing is sent until you choose; one character, or a **Character**
  set on the node, goes straight in. Picking the first silently would
  log you in as the wrong character, which is not a thing to discover
  after the fact.

  Verified over a loopback socket: connects, receives GetLogin, sends
  credentials, receives LoginOK. Everything past that - the character
  handshake, rooms, real objects - needs the live server.

  Movement in the live view is the library's own `BaseClient.TryMove`
  and `TryYaw`, not a hand-rolled step. That was worth finding: it
  denies movement while resting or paralyzed, refuses to run on low
  vigor, knows about the wolfpack buff and the movement-speed percent,
  slows you in deep water, collides with objects flagged no-move-on as
  well as with walls, slides using the room's own `VerifyMove`, and
  starts the move so `BaseClient.Update` sends it. The version it
  replaced did none of that.

  The offline view has no client, so it still uses
  `WorldSync.TryMove`, which is the room collision plus a slide.

  **The client's clock has to be advanced by hand.** `RootClient.Tick`
  does `GameTick.Tick()` and then `Update()`, and its own loop is the
  only thing that normally calls it - but that loop sleeps, and Godot
  owns the frame timing here, so the view calls the two itself.
  Calling `Update` alone leaves `GameTick.Span` at zero for ever, and
  `Span` is what every timed thing in the library multiplies by:
  `TryMove` computes its step as direction x speed x span, so the avatar
  simply would not move. Nor would objects interpolate, animations
  advance, or the request rate limiters ever come round.

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
- **See-through walls** - grates, railings, fences, open doorways - are
  drawn with their transparency and you can look past them. The room
  says which walls those are: `WF_TRANSPARENT` means "has some
  transparency" and `WF_NOLOOKTHROUGH` means "even so, you cannot see
  past it". 33788 of the 40586 two-sided walls carrying a middle texture
  across all 362 rooms are the see-through kind.

  They used to be drawn as solid walls, and not even quietly: room
  textures have their alpha forced opaque, because a floor has no holes
  in it, so the palette's transparent index came out as a sheet of bright
  cyan. `Tools/Meridian59.Net8Fpv --solid` renders the old way if you
  want to see it.

  They are collected during the column walk and drawn after the sprites,
  far to near, because the walk goes front to back and something you can
  see through has to be painted over what is behind it. "Behind it" is
  the whole difficulty: drawn after the sprites with no further
  information, a grate covers creatures standing *in front* of it too.
  So the sprite pass records a depth per pixel and a grate skips any
  pixel a nearer sprite already owns.

  `Tools/Meridian59.Net8RenderCheck -- grate` walks a test sprite
  through the crypt fence in toscrypt2 and measures what survives,
  against the sprite's own 1/d^2 falloff rather than an absolute count.
  In front of the fence it keeps 102% of the prediction; behind it, 56%.
  Removing the depth test drops the front one to 87% and the check
  fails, which is how the check was confirmed to test anything.

  `Tools/Meridian59.Net8RenderCheck -- seethrough` measures it: 14 of
  362 rooms change from the camera positions it samples, by up to 46% of
  the pixels, and the share of columns that close goes from 94.24% to
  94.22% over 17184 views - so letting people see through fences does
  not leave holes in the picture.

- **Sidedef flags.** `WF_BACKWARDS` - "draw bitmap right/left reversed" -
  is honoured; it is set on 1108 of the 12874 sidedefs, across 232 of the
  362 rooms, and was previously ignored. **This one wants your eye**:
  the flag's meaning is unambiguous in the game's own source, but the
  result cannot be confirmed here against anything. `Net8Fpv --noflip`
  and `Renderer.HonourBackwards` turn it off.

  `WF_NORMAL_TOPDOWN`, `WF_ABOVE_BOTTOMUP` and `WF_BELOW_TOPDOWN` are
  honoured too - they choose whether a wall part's texture is anchored
  at its top or its bottom, and they came with the UV port rather than
  as guesses.

  `WF_NO_VTILE` stops a middle texture repeating up the wall, and is
  applied only where the wall is actually drawn see-through. The flag's
  own comment says it "must be transparent", but 235 of the 2301
  sidedefs carrying it are not flagged so, and clipping a solid wall
  would leave a hole you can see the void through. A tiled texture is
  the better of those two wrongs. Not visually confirmed: the rooms
  using it here want textures that are not in the resource folder this
  was developed against.

- **Floors and ceilings.** The scale here really is a flat 1/1024 with no
  shrink involved - `RooSubSector.UpdateVertexUV` confirms it - and the
  axis swap is the same as walls. The per-sector texture offset is now
  applied; it had been ignored, and 1420 of the 30806 sectors carry one.

  Two gaps remain, both with numbers so they can be judged:

  **Sloped floors and ceilings** - 4674 of the 30806 sectors - are drawn
  as slopes. A sector's plane is `Ax + By + Cz + D = 0`
  (`RooSector.CalculateSlopeHeight`), and a column renderer turns out to
  be a good fit for it: each column already has its own ray, so the
  wall's top and bottom come from the sector's height *at that column's
  hit point*, and the fill solves the ray against the plane for one
  division per pixel, the same cost as the flat case.

  The algebra is the part that could be quietly wrong - a sign error
  there puts a slope's texture somewhere plausible but not where the
  geometry is - so `Net8RenderCheck -- slope` checks the closed form
  against a bisection that just walks the ray until it crosses the
  plane: 134887 rays, none disagreeing, worst relative error 1.5e-4.

  Still approximate: a flat span between two hits takes its screen
  extent from the heights at the hits, so a slope can show a small seam
  where the span ends. Per-leaf flats would remove it.

  **Texture anchoring in negative coordinates.** The library measures
  flat UVs from the most top-left vertex of each BSP leaf, which is
  (0, 0) for any leaf that sits entirely in positive space - so this
  renderer's world coordinates match it exactly there. 199 of 362 rooms
  have geometry at negative coordinates, and in those regions the
  anchoring differs. Doing it properly means per-leaf flats rather than
  per-sector, which is the same restructure the slopes want.

- **Scrolling floors, ceilings and walls** - water, lava, the odd moving
  wall - animate off `Renderer.Time`, which the views advance each frame
  and which is zero everywhere else, so every offline render and every
  check in `Tools/` still produces the same picture it did.

  1788 sectors scroll their floor, 5 their ceiling, and 172 sidedefs
  scroll a wall. The rate comes from `RooSector.GetSectorScrollSpeed`
  and its sidedef twin, which are `protected` and so had to be ported
  rather than called. Note that the two use different constants - a
  sector scrolls at 12/6/2 ms per pixel and a wall at 96/32/8, so a
  wall's "fast" is slower than a sector's "slow".

  `Net8RenderCheck -- scroll` renders every affected room at two times
  and counts the ones whose picture moves. Of 129 such rooms, 3 have
  their scrolling texture present in the resource folder this was
  developed against, and those 3 are exactly the 3 that move - the rest
  are water and lava rendering as missing-texture grey.

  What is *not* confirmed is which way things flow: mapping compass
  directions onto texture axes is a sign convention, and nothing here
  can check it. If a river runs backwards in game it is one line.

- **Animated wall textures.** 55 rooms have a sidedef with an animation
  on it, and this renderer showed frame 0 of every one - a torch that
  never flickers. The library drives these by moving the sidedef's
  animation to another group and picking a different frame of the *same*
  file (`RooSideDef` does `GetFrameIndex(animation.CurrentGroup, 0)`),
  so the texture cache is now keyed on file and frame rather than file
  alone. The live view gets the animation ticked by
  `BaseClient.Update`; the offline view ticks the room itself, since
  there is no server to do it.

  `Net8RenderCheck -- anim` reports honestly that none of those 55
  rooms' art is in the resource folder here, so it cannot show a room
  animating. It checks the mechanism directly instead: of the 6
  multi-frame textures that *are* present, all 6 hand back a different
  picture for a different animation group.

  Sector animations are heights, not textures - lifts and moving floors.
  Those already work in the live view, because the renderer reads a
  sector's height fresh every frame.

- **Sector lighting is not implemented**, and shading is distance fog
  alone. The data is there: `RooSector.Light1`, where 0-127 means the
  sector lights itself (brightness `Light1 / 128`, so 0 is black and 127
  is full) and 128-255 means it takes the room's ambient light scaled by
  `(Light1 - 128) / 64`. Of the 30806 sectors, 26257 take ambient light -
  17005 of them at the 192-207 that scales it by 1.0 - and 4549 light
  themselves, 557 of those at almost nothing, which is how a cave is
  meant to be dark.

  Not done on purpose. Nothing in this repo consumes that model - the
  Ogre client did its lighting in the C++ part, through Ogre's own
  lights - so there is no formula to port and nothing to check a guess
  against, and the ambient level itself only arrives from the server.
  Inventing a curve here would be the third time today I built on a
  premise I could not test. It wants the server's ambient value and
  somebody who knows what these rooms are supposed to look like.

## A note on the resource layout

`BaseClient.Init` expects this project's own layout - `strings`,
`rooms`, `bgftextures`, `bgfobjects`, `sounds`, `music`, `mails` as
subfolders of the resource path. An installed Meridian client is flat:
everything sits in `resource` together. `M59Client.Init` looks before
assuming, and falls back to flat.

It looks for *files*, not for the folder, and that matters:
`ResourceManager.Init` creates whatever is missing as it goes, so one
run with the wrong layout leaves seven empty subfolders behind, and a
check for the folder alone would then pick the wrong layout for ever
afterwards. That is not hypothetical - it is what happened while
testing this, and the test folder here still has the empty folders in
it, which makes it the better test.

Getting this wrong does not produce an error. It produces a world with
no textures in it.

The string file is found the same way rather than assumed.
`BaseClient.Connect` selects a string dictionary by the name in the
connection entry, and this hardcoded `rsc0000.rsb` - the usual name, not
a guarantee. Get it wrong and every room and object name the server
sends resolves to nothing, again with no error. `M59Client.FindStringDictionary`
looks in the strings folder and then the resource folder, and falls back
to the usual name only if there is nothing to find.

## A note on case

Asking the resource manager for a file by a different casing than the
one on disk used to throw `FileNotFoundException` on anything that is
not Windows - which is to say, on the phone. The dictionaries are
case-insensitive so the lookup succeeded, but the load then built its
path from the name that was *asked for*, and opening that path is
case-sensitive on Linux and Android. The object folder has mixed casing
in it (`Knight.bgf`, `ogreX.bgf`, `duskrat.bgf`), and the server's names
need not match.

`ResourceManager` now resolves through a map of any-casing to the casing
on disk before building a path, and `Tools/Meridian59.Net8Verify` checks
it. That check needs a fresh `ResourceManager` per attempt: asking with
the right casing first loads and caches the file, and every later casing
then finds it in the dictionary without going near the disk. Written the
other way round - which is how it was written first - it reports that
everything is fine.

## Unit conventions

Established by measurement, not documentation - see
`Tools/Meridian59.Net8RoomTex/README.md`:

- Wall X/Y are FINENESS units, 1024 per grid square.
- Sector heights and `ClientLength` are in XY/16 units (measured:
  `xyLength / ClientLength == 16.0` exactly across barinn.roo).

The third one used to read "one texture tiles across one grid square, so
UV = xy / 1024", and it was an over-generalisation from one room. Wall
textures scale by the texture's own shrink factor over its size:
`along * shrink / textureHeight` across a wall and
`height * shrink / (textureWidth * 16)` up it. That reduces to xy/1024
exactly when size over shrink is 64, which 128x128 shrink 2 and 64x64
shrink 1 both are, and which is most of what is in the Barloque inn -
so the measurement was right about the room it was taken in and wrong
in general. `Tools/Meridian59.Net8Uv` checks the rule against the
library's own `RooWall.GetVertexData`.
