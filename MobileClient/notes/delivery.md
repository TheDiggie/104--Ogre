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

## Two commits are unsigned, and that is now a decision, not a backlog
Tags: process | SETTLED 2026-10-01: signed, then deliberately UNSIGNED again, because origin already had the originals

The stop hook keeps naming 0462f0e and f522e2c. It is answered.

What happened when it was finally done: the rebase signed all 304
commits and the content came out byte-identical, verified both sides.
Then the push revealed what a stale local ref had hidden - origin
ALREADY HELD those two commits, so GitHub Desktop's pull tried to merge
the pre-rebase history back in and hit real conflicts in SceneShot.cs
and harness.md. Aborted.

Ashton chose to undo the signing rather than force-push, so GitHub's
published history was never rewritten: everything was replayed onto
origin's own f522e2c with `git rebase --onto`. All the work above is
signed; those two are not, by choice. Do not re-sign them.

THE LESSON THAT COST THE HOUR: `git rev-list origin/net8-core...` reads
the LOCAL cached ref, which here was days old and said origin was
behind. It was not. Check the real remote - GitHub Desktop's counter,
or a fetch - before claiming nothing published will be rewritten.

See also: the bundle flow -> this file | pushing -> this file

## Pushing needs GitHub Desktop
Tags: process, gotchas | The sandbox proxy refuses the repo and the device VM has no credentials; the push happens in the GUI

Two dead ends, both worth not rediscovering:

- From the container: `remote: access denied by the git proxy:
  TheDiggie/104--Ogre is not in this session's authorized repository
  set`. Adding the repo to the session's sources would fix it.
- From `device_bash`: `could not read Username for https://github.com`.
  That shell is an isolated Linux VM; the credentials live in GitHub
  Desktop on Windows.

So: computer use, GitHub Desktop, Repository > Push. It is granted at
tier "full" (not click-only like the shell and Explorer), so its menus
work normally. An Explorer window in front of it makes every click fail
with "the desktop shell is frontmost" - `computer_open_application` on
"Githubdesktop" raises it, and File Explorer has to be granted too
before a click can land while the shell has focus.

See also: the bundle flow -> this file

## Verify every bundle by checksum
Tags: process, gotchas | Binary files crossing device_commit_files were corrupted twice on 2026-10-01; base64 round-trips cleanly

Two bundles arrived with a different md5 than they left with - one of
them carrying a commit hash that exists nowhere in the history. The
failure is silent: git still reads the file as a bundle, and the merge
says "Already up to date" instead of fast-forwarding, which is the only
reason it was caught.

So bundles travel base64 now:

    base64 -w0 x.bundle > x.b64            # container
    device_commit_files x.b64
    tr -d '\r\n' < x.b64 | base64 -d > x.bundle   # device_bash
    md5sum x.bundle                        # and compare, every time

The `tr` matters: something in the path translates line endings, which
is the likeliest cause of the corruption in the first place.

See also: the bundle flow -> this file

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

WHY IT KEPT DYING, fixed 2026-10-01: a `goto` whose label sat inside a
parenthesised `if exist ... ( ... )` block kills cmd.exe outright - no
error, no log line. It managed exactly one build per launch and then
vanished. Every label is at the top level now and every branch out of a
condition is a parenless `if ... goto`; it has survived several builds
since. If it dies again, that is a new cause, not this one.

The APK is `m59_tmp\Meridian Mobile Client.apk` from 2026-10-01 (it was
`Meridian59-test.apk`). `%OUT%` is quoted at both uses in build-apk.bat,
so the spaces are safe there - but a URL cannot carry a raw space, so
the manifest spells them `%20`.

See also: the bundle flow -> this file | updates -> ./mobile-client.md

## device_commit_files will not overwrite a path it already wrote
Tags: gotchas | Second commit to the same name reported written and changed nothing; use a new filename per bundle

Caught on 2026-10-01. The first bundle was cut from the wrong base, so
a corrected one was committed to the SAME path. The call answered
`{"written":[...],"rejected":[]}` and the file on disk was still the old
one - same size, same md5, same mtime. Nothing failed and nothing
happened.

This is the second way this step has lied (the first was silent binary
corruption, which is why everything goes as base64 with a checksum
now). The md5 compare caught both. Never skip it, and never reuse a
bundle filename within a session: night-12b, not night-12 again.

See also: the bundle flow -> this file

## latest.json is generated by the watcher now, and must not carry a BOM
Tags: process, gotchas | The version lives in project.godot only; a BOM on the manifest silences the update prompt entirely

Written by hand until 2026-10-01, which meant the version existed in
two places and the watcher only ever updated one of them. It is now
`C:\Users\ashto\m59_tmp\make-latest.ps1`, called by the watcher after a
build whose rc is 0, reading `config/version` out of
MobileClient/project.godot - the same setting Updater.Running reads at
runtime. The notes come from `release-notes.txt` (first non-empty
line), written by a person per release; the url never changes, which is
why the APK is always "Meridian Mobile Client.apk".

THE BOM. The watcher calls `powershell`, which is 5.1, whose
`Set-Content -Encoding UTF8` writes a byte order mark. Godot's
JSON.parse_string returns Nil on a document that starts with U+FEFF -
checked, not assumed: type 27 for the plain string and the trimmed one,
type 0 for the BOM'd one - and Updater's catch then swallows the whole
check. A manifest that looks perfect in an editor offers nobody an
update. So the script writes with
`[System.IO.File]::WriteAllText(..., New-Object System.Text.UTF8Encoding $false)`,
and Updater.Answered TrimStarts U+FEFF as well, because the file lives
on a server somebody may hand-edit.

Only on rc=0: a manifest naming a version whose APK failed to build
sends every player to yesterday's file.

See also: the updater -> ../Updater.cs

## The resource manifest
Tags: process, gotchas | Resources live on the site now, not in the APK; Tools/make-resources.ps1 stages them and writes resources.json, which goes up LAST

The 500 MB APK was 467 MB of resource folder - 4,700 sprites, rooms
and sounds - shipped again to every phone on every build. They live at
https://meridian59.us/mobile/resources/ now. A fresh install fetches
them once; every later launch fetches `resources.json`, compares each
file's hash with what it holds, and downloads only what changed. The
APK is ~30 MB.

`Tools/make-resources.ps1` makes the site side. Run it with
`powershell` (it is 5.1-safe, same BOM-free WriteAllText as
make-latest.ps1) against the resource folder and a staging folder,
`C:\Users\ashto\m59_tmp\site` by default, which mirrors the site's
`mobile\` directory: `site\resources\` holds the files, `site\
resources.json` is the manifest. The summary line it prints - files,
bytes, hashed fresh vs cached, copied, deleted, stamp - is the thing
to read; a second run says 0 copied, 0 hashed fresh, same stamp.

THE FORMAT is a contract with the client, written in parallel against
it, so it does not change on one side alone:

    { "stamp": "<sha1>", "count": N, "bytes": N, "base": ".../mobile/resources/",
      "files": [ { "n": "name.bgf", "s": size, "h": "<sha1, lowercase>" }, ... ] }

Files sorted by `n` ordinal (not culture). `stamp` is the SHA-1 of the
lines `<n>\t<s>\t<h>\n` in that order, so the client can tell "nothing
changed" with one string compare. `n` is written raw - the client
percent-encodes it when it builds `<base><n>` - and is JSON-escaped,
nothing more. The JSON is built with a StringBuilder, not
ConvertTo-Json, which on 5.1 takes tens of seconds over 4,700 objects
and whose -Depth default flattens quietly instead of failing.

THE WHITELIST. Top level only, because the library reads one flat
folder (ResourceManager.Init gets the same root seven times), and only
`bgf roo ogg wav mp3 rsb bsf xml png`, case-insensitive. That drops the
`.import` sidecars Godot's editor leaves, the `.dll`s that landed
beside the data, `x.roo~` backups, the subfolders ("Claude outputs",
"dr_maps") and the extensionless junk - a 38 MB temp file called
zidUkPjp sits in the folder today and would have been the biggest
download. The staging folder is an EXACT mirror: what is not selected
is deleted from it, so junk never reaches the site and a removed
resource leaves it. Zero files selected, or no source folder, is exit
1 and no manifest: an empty manifest tells every phone to delete
everything.

THE CACHE. `site\.hashcache.json` remembers (size, mtime ticks, sha1)
per name; an entry is reused only when size and mtime both match, so
a rebuild that touched three files hashes three. Delete the file to
force a full rehash. It rides up to the site with the rest of the
folder and is harmless there - it says nothing the manifest does not.

THE FILEZILLA STEP, and the order matters. Select the whole `site`
folder locally, drag it onto the site's `mobile/` directory, and
answer the overwrite prompt with "overwrite if different size or
newer" applied to all - the script stamps each staged copy with the
SOURCE file's mtime for exactly this reason, so only the delta moves
instead of 467 MB every time. Then upload `resources.json` LAST, by
itself, after the transfer queue of `resources/` has drained. A phone
that fetches a new manifest while the files it names are still in the
queue fails those downloads, and the CDN caches by URL, so a 404 it
saw once can keep being served after the file lands. Files first,
manifest after; never the other way round.

See also: the script -> ../../Tools/make-resources.ps1 | latest.json and the BOM -> this file | the updater -> ../Updater.cs
