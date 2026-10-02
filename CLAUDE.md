# 104--Ogre: read this before anything else

This repo is run from Ashton's control room, Rootstock at
`C:\ClaudeBrain` (in a cloud session: `~/mnt/ClaudeBrain` through the
device shell). It is not optional and it is not a thing to call at the
end: it is where every session starts and where every lesson lands.

## Every session, in this order

1. `python3 tools/standup.py` in ClaudeBrain. Read it. Its [DO]
   proposals are work for this session, not a backlog.
2. `Grep "^## " LESSONS.md` there for the task shape before starting
   the task. Do the one right way first.
3. Work. Fan the long jobs out to employees (SUBAGENTS.md); every
   brief names the files it may and may not touch.
4. When a lesson is learned, write it into `LESSONS.md` IN THE SAME
   BATCH, in the entry shape (Tags, Keys, THE ONE RIGHT WAY, TRIED /
   DO INSTEAD, See also). Not later. Not only in MobileClient/notes.
5. Append the day's file under `docs/history/days/` with WHERE WE
   LEFT OFF, and run `python3 tools/run_all.py check` before stopping.

## The laws (ClaudeBrain/CLAUDE.md has them; they bind here too)

Short answers. Keep going. In-page modals only. Ask once, then decide
and say the assumption. One home per fact. Flag a contradiction, never
silently comply or refuse. Look at the output before sending it.

## This repo's own notes

`MobileClient/notes/` holds what is specific to this client - the
harness, delivery, Godot traps, the rulings. THE LIBRARY IS THE
SPECIFICATION: `Meridian59/` and `Meridian59.Ogre.Client/` answer
every behavioural question, cited to file:line.
