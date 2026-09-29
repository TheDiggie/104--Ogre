# The play harness (SceneShot)

`MobileClient/SceneShot.cs` runs the client headless and drives it, which
is how every real bug in this port was found. Diffing against the
reference found the cosmetic ones; playing found the ones that made the
client unusable.

## Running it
Tags: process | xvfb-run plus M59USER/M59PASS; --press is a comma list of steps, --shots writes out-1.png per step, --char skips the picker

Godot lives at
`/tmp/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.
Steps in `--press`: a bare node name, `@tap:XxY`, `@hold:<Node>`,
`@type`, `@submit`, `@name:<Node>`, `@slot`.

Fixture switches on the fake server: `M59_STATCHANGE=1`, `M59_NEWS=1`,
`M59_CHATFLOOD=1`.

See also: the fixture -> fake-server.md

## The invariant tour
Tags: process, lessons | Build a sequence where step N leaves the world exactly as step 1 did, shoot every step, stack the same crop from each frame, and look - three bugs came out of it

Anything that differs between the first frame and the last is a bug you
would otherwise have to notice by luck.

See also: the client -> ../README.md

## Two harness bugs that hid real ones
Tags: lessons, gotchas | Read the box text BEFORE emitting the submit signal, and match buttons on IsVisibleInTree() not Visible

The submit step printed the text after emitting, and the handler clears
the box - so every successful send reported `submitted ""`, which hid the
fact that plain text sent nothing at all. Separately, matching on a
node's own `Visible` flag pressed buttons inside hidden parents. Both are
fixed; both cost days.

See also: SceneShot.cs

## What has been played end to end
Tags: process | Kept so the next session tests something new rather than re-proving these; each was watched on the wire, not just on screen

- Every bottom-row panel opens and closes; Map is a toggle with no Close,
  which is right.
- Chat: plain text sends `SayTo` with ChatTransmissionType.Normal and the
  room answers.
- Targeting: tap outlines in red, tapping elsewhere clears it, and the
  library resets TargetID itself when the target leaves the room.
- Attacking: three hits, `Remove`, the object goes and the outline with it.
- Buying: `ReqBuyItems` then `InventoryAdd`; a stackable carries its
  count, a single item sends x0. Two stacks of one item do NOT merge, and
  that is the game - hence `NumOfSameName`.
- Looting: `ReqGet`, the item lands in the pack and leaves the floor, and
  pressing Loot again opens nothing because there is nothing to loot.
- Trading: `ReqCounterOffer` with the right id and count, the server
  confirms your side, the buttons collapse to Cancel. Accept stays hidden
  on a background offer, as the reference does.
- Stat change: the steppers respect the pool, OK validates intellect
  against the schools, the confirmation is an in-page modal, and Yes
  sends `ChangedStats` (157).
- Chat flood: 205 lines, the full log scrolls and closes.

See also: the fixture -> fake-server.md | the panels -> mobile-client.md

## Drags, and why they were missing
Tags: process, lessons | @drag:<from>><to>@<frames> presses, slides over ten frames, HOLDS, then lifts - without a hold the movement stick reports nothing and the one interaction a phone client is for was untestable

`@tap` could already be scripted. The movement stick and the look drag
are both holds: a stick that is put down and lifted in one frame reports
no direction at all, so walking, turning and looking had never once been
exercised by a scripted run, only by hand.

`Relative` matters as much as `Position`. The look half turns by the
DELTA, so a drag that sets only Position turns the camera once and then
sits still for the rest of the hold.

Proved with `@drag:270x1500>270x1100@90` (walk forward: 22 movement
messages on the wire) and `@drag:1100x700>1500x700@60` (look right: the
view ends facing a different wall).

See also: the touch layer -> TouchControls.cs | the client -> mobile-client.md

## A run inherits the last run's hotbar
Tags: gotchas, process | HotbarStore saves to user://hotbar.cfg per character, so every scripted bind is still there next run - the slots accumulate across a testing session

Working as designed, and a trap for anyone reading a screenshot: after a
few runs that bind a spell, the hotbar shows eight seeded actions plus
one slot per bind, and `hot{N}` names shift with them. Delete
`user://hotbar.cfg` (under the Godot user data folder) for a clean start.

See also: HotbarStore.cs
