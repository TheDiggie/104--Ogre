# Notes and rulings

Standing rulings that are not laws in the core and not big enough for a
topic file. One section each; headings are search keys.

## Rulings by Ashton
Tags: process | The standing instructions that govern how work is done here

- Work continuously on long jobs; do not stop to report after every step.
- Short, punchy answers. No long paragraphs.
- In anything built for him, use in-page modals, never browser dialogs.
- A target that is clicked to attack is outlined in red, as the game does.
- Use the agents. Long jobs are fanned out, not walked through alone.
- The game is played in LANDSCAPE ONLY. Portrait is not a case to design for.
- LOOK AT THE OUTPUT before sending it. Open the screenshot, read the
  frame, fix what is wrong - do not hand over a draft nobody has looked at.
- DO NOT TAKE HIS SCREEN without asking, and release the lock afterwards.
  He games on that machine. The file bridge needs no screen; use it.

See also: the core -> the client -> ../README.md

## Open questions
Tags: process | Things deliberately parked, so they are not rediscovered as bugs

- Autorun and the Actions window had nowhere to live while the bottom
  row was full at 1080 wide. Settled: the row is a drawer, Acts is a
  tile in it, and Auto is pinned to the left edge because it is
  movement. See MenuDrawer.cs.
- Two Attacks when a target is selected - the cluster disc fires the
  Attack action, the target row sends the request directly. Raised, not
  yet answered. Both work; they are not the same send.
- Auto sits on the left edge, which is technically the walking thumb's
  half. Raised, not yet answered; it is above where a thumb rests.

SETTLED since: the unsigned commits (signed, then deliberately unsigned
again - see delivery.md, and do not reopen it).

See also: delivery -> ./delivery.md | the client -> ./mobile-client.md

## The library is the specification, and an oracle beats an opinion
Tags: process, lessons | Every renderer bug found since has been found by a check that compares against the library, not by looking at the picture

The checks under `Tools/Meridian59.Net8RenderCheck` and
`Tools/Meridian59.Net8Uv` exist because a picture that looks right can
be wrong in ways nobody sees until a player stands in the wrong place.
Three separate bugs were invisible to the eye and obvious to a count:
sprites drawn through walls (237556 pixels across 720 scenes), the
wall texture's vertical origin (363 of 76136 wall ends), and
WF_NO_VTILE, whose 3699 walls the UV oracle had SKIPPED since it was
written - so the one part of the model still unported was also the one
part never checked.

When a check skips a case, that case is where the bug is. Make the
oracle cover it before trusting the code.

See also: the client -> ./mobile-client.md

## Rootstock is where the work is run from
Tags: process | Standup first, LESSONS.md in the same batch, day file and check before stopping - see /CLAUDE.md at the repo root

Ashton, 2026-10-02, after reminding twice: "why do I keep having to
tell you to use the rootstock. make it part of everything you do."
The repo-root CLAUDE.md now says what that means in order; this entry
exists so a grep of the rulings finds it too. A lesson written only in
these notes is a lesson the next project never sees.

See also: /CLAUDE.md | C:\ClaudeBrain\CLAUDE.md
