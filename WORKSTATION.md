# Workstations

PURPOSE: What each machine that works on this repo must have installed
  for the build, the headless play harness and the delivery path to work,
  and which machine is which.
INTENT: a missing tool should be found by a survey on day one rather than
  by a build that fails halfway through a long unattended run.

Search keys: workstation, machine, setup, prerequisites, survey, WS1.

## WS1 - Ashton's desktop (Windows)
Tags: process | The machine that holds the repo and runs the game; the bridge VM is a separate, Linux, machine and the survey reports whichever one it runs on

`C:\Users\ashto\OneDrive\Documents\GitHub\104--Ogre`, branch `net8-core`.
Needs: .NET 8 SDK, Godot 4.7 mono, Python 3.10+ with Pillow, git with
user.name and user.email set. Optional: the Android SDK for the phone
export, GitHub CLI.

`python tools/run_all.py survey` prints the list and what is missing. It
is deliberately outside the `session` group, because the bridge VM that a
cloud session reaches is a different machine with a different answer.

See also: the survey -> tools/workstation_survey.py | delivery -> docs/systems/delivery.md

## The cloud workspace
Tags: process | Ephemeral; holds a clone at /home/claude/ogre104 and the Godot binary under /tmp - nothing there survives the session

Work done there reaches WS1 as a git bundle, never a push.

See also: docs/systems/delivery.md
