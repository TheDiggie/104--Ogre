# Delivering work to Ashton's machine

## Push is blocked; ship a bundle
Tags: process | The git proxy returns 403 on push, so work travels as a git bundle committed into ~/m59_tmp and fetched locally

    git bundle create /mnt/user-data/outputs/night-N.bundle <prev>..net8-core

then `device_commit_files` it to `~/m59_tmp/`, and on his machine:

    git fetch $HOME/mnt/m59_tmp/night-N.bundle net8-core:refs/remotes/bundle/night-N
    git merge --ff-only refs/remotes/bundle/night-N

Always fast-forward. A merge commit here means the two copies diverged
and something is about to be lost.

Bundle the BRANCH, not HEAD. `git bundle create f.bundle <prev>..HEAD`
writes a bundle whose only ref is `HEAD`, and the fetch above then fails
with "couldn't find remote ref net8-core". `<prev>..net8-core` writes
`refs/heads/net8-core` and works. `git bundle list-heads` says which you
made.

The two homes are not the same home. `device_commit_files` resolves `~`
to the Windows home, `C:\Users\ashto`; `device_bash` resolves `~` to
its own session directory and reaches the machine only through
`~/mnt/<connected folder>`. Commit to the absolute Windows path
(`C:\Users\ashto\m59_tmp\x.bundle`) and fetch from `~/mnt/m59_tmp/x.bundle`.
A commit to `~/m59_tmp/...` succeeds and lands somewhere `device_bash`
cannot see.

See also: the client -> ../README.md

## Two commits are unsigned and stay that way
Tags: process | 0462f0e and f522e2c are unsigned; only a rebase fixes them, and a rebase desyncs Ashton's working copy

The stop hook will keep asking. The answer is no until he is at the
keyboard and wants to pay for it. The count and the hashes change as
history grows - what does not change is that they sit under everything
since, so the rewrite is the whole branch and his clone has to be reset
to match in the same sitting.

See also: the bundle flow -> this file | the stop hook -> the running guide -> ../TRYING-IT.md

## The build watcher has to be running
Tags: process, gotchas | Writing build.trigger does nothing on its own - a batch file on Windows polls for it, and a reboot kills it

`m59_tmp\m59-build-watcher.bat` polls the repo for `build.trigger` every
ten seconds, deletes it and runs `m59_tmp\build-apk.bat`. Nothing in the
container can start it: `device_bash` is a Linux VM that cannot execute
a Windows binary, and computer use grants terminals in click-only mode,
so a command cannot be typed into one. After a reboot it is a
double-click from Ashton, once, and then builds are self-service again.

Check `m59_tmp/watch-build.log` and whether `build.trigger` still exists:
a trigger that is still there a minute later means nothing is watching.

See also: the bundle flow -> this file
