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

See also: ../../CLAUDE.md

## Two harness bugs that hid real ones
Tags: lessons, gotchas | Read the box text BEFORE emitting the submit signal, and match buttons on IsVisibleInTree() not Visible

The submit step printed the text after emitting, and the handler clears
the box - so every successful send reported `submitted ""`, which hid the
fact that plain text sent nothing at all. Separately, matching on a
node's own `Visible` flag pressed buttons inside hidden parents. Both are
fixed; both cost days.

See also: SceneShot.cs
