# Workflows

PURPOSE: The repeatable multi-step processes of this project, each named
  so a prompt that matches one is routed to it instead of improvised:
  building and checking the mobile client, playing it headless, and
  delivering work to Ashton's machine.
INTENT: a process performed twice should be written down once, so the
  third time costs a lookup rather than a rediscovery.

Search keys: workflows, build, play, deliver, regression, routes.

## Build and check the client
Tags: process | dotnet build then GREP THE OUTPUT FOR "error" - the tail shows elapsed time only and will report success over a failed build

1. `dotnet build MobileClient/MobileClient.csproj 2>&1 | tee /tmp/build.log`
2. `grep -i error /tmp/build.log` - this step is the check, not step 1.
3. `python tools/run_all.py check`

See also: the verify block -> CLAUDE.md

## Play it headless
Tags: process | Start the fake server, confirm it is alive, then drive SceneShot with --press and --shots

1. Start `Tools/Meridian59.Net8FakeServer`; `pgrep` it and read
   `head -3 /tmp/fake.log`. A dead server returns flat grey frames that
   look exactly like a broken client.
2. `xvfb-run` the Godot binary with `M59USER`/`M59PASS`, `--char`,
   `--press <steps>` and `--shots`.
3. Look at the WHOLE frame before cropping. Twice a crop has hidden the
   answer - the look panel renders at the top of the screen.

See also: docs/systems/harness.md | docs/systems/fake-server.md

## Deliver a batch to Ashton's machine
Tags: process | Bundle, commit into m59_tmp, fetch and fast-forward - push is blocked by the proxy

See also: docs/systems/delivery.md

## The invariant tour (a regression pass)
Tags: process | A step sequence where the last step leaves the world as the first did; stack the same crop from every frame and look

See also: docs/systems/harness.md
