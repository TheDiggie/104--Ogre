# Pushes the site folder to the website over SFTP, delta only, in the
# order the client needs.
#
# WHY A KEY AND NOT A PASSWORD. The watcher runs unattended, so whatever
# it authenticates with has to live on disk. A password on disk is a
# password; a private key on disk is a key that opens exactly one door
# and can be revoked from the hosting panel without changing anything
# else. OpenSSH's sftp with -o BatchMode=yes refuses to prompt, which is
# what makes this safe to run from a script: if the key is missing or
# rejected it fails at once instead of hanging on a password prompt
# nobody will answer.
#
# WHY DELTA. The game data is 4,600 files and 428 MB. Uploading all of it
# after every build would take forty minutes and hammer the host for a
# change to three sprites. <Site>\.uploaded.json records the SHA-1 of
# every file as it was when it last went up successfully (the hashes
# come from make-resources' .hashcache.json, so nothing is hashed twice);
# only files whose hash differs, or that have no record, are sent. The
# record is written only when the whole batch succeeded, so a dropped
# connection means the next run sends the same set again rather than
# believing half of it arrived.
#
# WHY THE ORDER. latest.json is the trigger: the moment it names a new
# version, every phone that opens the game is sent for the APK, and the
# new APK immediately asks for resources.json and then the files it
# lists. So: resources first, the APK, then resources.json, and
# latest.json LAST. Anything else hands a player a 404 mid-update.
#
# A removed file is deleted on the server too, so the site mirrors the
# staging folder - make-resources already pruned the junk locally, and
# a stale file left on the server is harmless to the client (it only
# fetches what the manifest names) but wastes the host's disk.
param(
  [string]$Site   = "C:\Users\ashto\m59_tmp\site",
  [string]$Key    = "$env:USERPROFILE\.ssh\m59_site",
  [string]$User   = "u279449782",
  [string]$HostName = "82.197.83.115",
  [int]$Port      = 65002,
  [string]$Remote = "/home/u279449782/domains/meridian59.us/public_html/mobile",
  [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Fail($msg) { [Console]::Error.WriteLine("upload-site: $msg"); exit 1 }

if (-not (Test-Path $Site)) { Fail "no site folder at $Site" }
$cache = Join-Path $Site ".hashcache.json"
if (-not (Test-Path $cache)) { Fail "no .hashcache.json in $Site - run make-resources first" }
if (-not $DryRun -and -not (Test-Path $Key)) {
  Fail "no key at $Key - run the setup in notes/delivery.md ('Let the watcher upload')"
}

# The hashes make-resources already computed: name -> @{ s; t; h }.
$have = @{}
$raw = Get-Content -Raw -Path $cache | ConvertFrom-Json
foreach ($p in $raw.PSObject.Properties) { $have[$p.Name] = $p.Value.h }

# What last went up: name -> sha1. Absent means nothing is known, and
# everything goes.
$uploadedPath = Join-Path $Site ".uploaded.json"
$sent = @{}
if (Test-Path $uploadedPath) {
  $u = Get-Content -Raw -Path $uploadedPath | ConvertFrom-Json
  foreach ($p in $u.PSObject.Properties) { $sent[$p.Name] = $p.Value }
}

$changed = New-Object System.Collections.Generic.List[string]
foreach ($name in ($have.Keys | Sort-Object)) {
  if (-not $sent.ContainsKey($name) -or $sent[$name] -ne $have[$name]) { $changed.Add($name) }
}
$removed = New-Object System.Collections.Generic.List[string]
foreach ($name in $sent.Keys) { if (-not $have.ContainsKey($name)) { $removed.Add($name) } }

# sftp batch syntax: one command per line, a leading "-" means "ignore
# this command's failure" (used for the mkdir, which fails harmlessly
# when the folder exists, and for rm of a file already gone). Paths with
# spaces are double-quoted. lcd/cd set both ends once so every put is a
# bare name and the batch stays readable in the log.
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("cd `"$Remote`"")
[void]$sb.AppendLine("-mkdir resources")
[void]$sb.AppendLine("lcd `"$Site`"")
foreach ($n in $changed) {
  [void]$sb.AppendLine("put `"resources/$n`" `"resources/$n`"")
}
foreach ($n in $removed) {
  [void]$sb.AppendLine("-rm `"resources/$n`"")
}
[void]$sb.AppendLine("put `"Meridian Mobile Client.apk`" `"Meridian Mobile Client.apk`"")
[void]$sb.AppendLine("put `"resources.json`" `"resources.json`"")
[void]$sb.AppendLine("put `"latest.json`" `"latest.json`"")
[void]$sb.AppendLine("bye")

$batch = Join-Path $Site ".upload.batch"
[System.IO.File]::WriteAllText($batch, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))

Write-Host ("upload-site: {0} changed, {1} removed, plus the APK and both manifests" -f $changed.Count, $removed.Count)
if ($DryRun) { Get-Content $batch; exit 0 }

# -o StrictHostKeyChecking=accept-new: the first connection records the
# host key, later ones verify it. A changed host key then fails the
# upload, which is the right answer to a server that is not the server
# it was.
& sftp -b $batch -P $Port -i $Key -o BatchMode=yes -o StrictHostKeyChecking=accept-new "$User@$HostName"
if ($LASTEXITCODE -ne 0) { Fail "sftp exited $LASTEXITCODE - nothing recorded as uploaded; the next run resends" }

# Everything landed, in order. Record it.
$out = New-Object System.Text.StringBuilder
[void]$out.Append("{")
$first = $true
foreach ($name in ($have.Keys | Sort-Object)) {
  if (-not $first) { [void]$out.Append(",") }
  $first = $false
  $esc = $name.Replace("\", "\\").Replace("`"", "\`"")
  [void]$out.Append("`"$esc`":`"$($have[$name])`"")
}
[void]$out.Append("}")
[System.IO.File]::WriteAllText($uploadedPath, $out.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host "upload-site: done"
