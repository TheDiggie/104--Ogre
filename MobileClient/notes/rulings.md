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

See also: the core -> the client -> ../README.md

## Open questions
Tags: process | Things deliberately parked, so they are not rediscovered as bugs

- The unsigned commits are not rebased until Ashton is at the keyboard:
  the rewrite would desync his working copy. See delivery.md.
- Autorun (the reference's AutoMove) and its Actions window - Dance,
  Point and GuildInvite - have nowhere to live: the bottom row is full
  at 1080 wide. Waiting on Ashton for where they should go.

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
