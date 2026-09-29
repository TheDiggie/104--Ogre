# 104--Ogre - the pointer core

Meridian 59 "Server 104": the classic client (C++/CLI + Ogre) plus a
.NET 8 port, and `MobileClient/` - a Godot 4.7 C# client built so the
game can be played on a phone. Work happens on branch `net8-core`.

Rootstock v1.33 installed 2026-09-29 | updates: ask

## The one law of this port
Index: core

THE LIBRARY IS THE SPECIFICATION. Every behavioural question is answered
by reading `Meridian59/` (the shared C# library) or
`Meridian59.Ogre.Client/` (the reference desktop client), never by
guessing and never by inventing. If the mobile client differs from the
reference, that difference is deliberate and says why in a comment.

## How to read this repo
Index: core

Grep headings first, read one section, never a topic file whole. One
door: `docs/index/MASTER_INDEX.md` lists every topic file, root file,
sub-index and rule, one line each.

## How to verify
Index: core

- Build: `dotnet build MobileClient/MobileClient.csproj` - then grep the
  output for `error`. The tail shows only elapsed time and will lie to you.
- Play it: `MobileClient/SceneShot.cs` drives the client headless with
  `--press` steps and `--shots`. See `docs/systems/harness.md`.
- Fake server: `Tools/Meridian59.Net8FakeServer`. `pgrep` it and check
  `head -3 /tmp/fake.log` before believing a blank frame.
- Checks: `python Tools/run_all.py check`

## The laws no hook enforces
Index: core

- CHECK THE FIXTURE BEFORE BLAMING THE CLIENT. Most "unimplemented"
  features were missing test data, not missing code.
- LOOK AT THE WHOLE FRAME. A crop that excludes the answer has twice
  concluded a working feature was absent.
- INVARIANT TOUR. Build a step sequence where step N leaves the world as
  step 1 did, shoot every step, stack the same crop, and look.
- PLAY IT, DO NOT DIFF IT. Reference parity found the cosmetic bugs;
  playing found the ones that made the client unusable.
- Delivery to Ashton's machine is by git bundle, not push. See
  `docs/systems/delivery.md`.

## The wiki convention
Index: core

Headings are search keys. Every section ends with a `See also:` line.
Hard-won sections carry `Tags: <tags> | <one-line takeaway>`. Every
write grows the web; never stop work for a library-wide pass.

## THE CONTRADICTION RULE
Index: core

If the CEO asks for something that contradicts a rule he previously set,
FLAG it and get explicit confirmation. Never silently comply, never
silently refuse.

See also: everything -> docs/index/MASTER_INDEX.md
