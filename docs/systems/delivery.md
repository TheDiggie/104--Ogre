# Delivering work to Ashton's machine

## Push is blocked; ship a bundle
Tags: process | The git proxy returns 403 on push, so work travels as a git bundle committed into ~/m59_tmp and fetched locally

    git bundle create /mnt/user-data/outputs/night-N.bundle <prev>..net8-core

then `device_commit_files` it to `~/m59_tmp/`, and on his machine:

    git fetch $HOME/mnt/m59_tmp/night-N.bundle net8-core:refs/remotes/bundle/night-N
    git merge --ff-only refs/remotes/bundle/night-N

Always fast-forward. A merge commit here means the two copies diverged
and something is about to be lost.

See also: ../../CLAUDE.md

## Six commits are unsigned and stay that way
Tags: process | ad081e5, 9987d81, ba89f9c, 135d25b, cce3efe, 4372226 are unsigned; only a rebase fixes them, and a rebase desyncs Ashton's working copy

The stop hook will keep asking. The answer is no until he is at the
keyboard and wants to pay for it.

See also: the bundle flow -> this file | the stop hook -> ../../HOOKS_METHOD.md
