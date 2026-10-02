# Stages the game's resource folder for the website and writes the
# manifest the mobile client downloads it by.
#
# WHY THIS EXISTS. The APK was 500 MB because the resource folder -
# some 4,700 files, 467 MB of sprites, rooms and sounds - was baked
# into it, and every build shipped all of it again to every phone.
# The resources now live on the site instead. A fresh install fetches
# them once; every later launch fetches resources.json, compares each
# file's hash with what it has, and downloads only what changed. So
# the manifest is the thing that makes a 30 MB APK possible, and the
# thing that makes a bad upload expensive: a manifest that lists a
# file the site does not have fails that download on every phone, and
# a manifest that lists nothing makes every phone delete everything.
# Both are guarded below.
#
# THE FORMAT IS A CONTRACT. The client is written against exactly the
# shape produced here - key order, "n"/"s"/"h", lowercase SHA-1, files
# sorted by name ordinal, stamp over "<n>\t<s>\t<h>\n" in that order.
# Change it on both sides or not at all.
#
# WHAT IT DOES, in order: pick the files; hash them (cached, so a
# rebuild that touched three files hashes three); mirror them into
# <Site>\resources\ EXACTLY - copy what differs, delete what is gone;
# write <Site>\resources.json. Then the owner drags <Site> onto the
# site's mobile\ directory with FileZilla, manifest LAST (see
# MobileClient/notes/delivery.md, "The resource manifest").
#
# PowerShell 5.1. The build watcher calls `powershell`, not `pwsh`, so
# nothing here may need 7: no ??, no ternary, no -Parallel, and the
# manifest is written with a BOM-free UTF8Encoding because 5.1's
# Set-Content writes one and Godot's JSON parser returns Nil on it
# (proven on latest.json; delivery.md has the numbers).
param(
  [string]$Source = "C:\Users\ashto\OneDrive\Documents\GitHub\104--Ogre\MobileClient\resource",
  [string]$Site   = "C:\Users\ashto\m59_tmp\site",
  [string]$Base   = "https://meridian59.us/mobile/"
)

$ErrorActionPreference = "Stop"

# Plain line on stderr and exit 1, so the watcher log says what refused
# instead of a red wall of PowerShell error record.
function Fail([string]$msg) { $host.UI.WriteErrorLine("make-resources: " + $msg); exit 1 }

if (-not (Test-Path -LiteralPath $Source -PathType Container)) {
  Fail "no resource folder at $Source"
}
# The client appends "resources/<n>" to this, so it has to end in a
# slash; a Base typed without one would produce ".../mobileresources/".
if (-not $Base.EndsWith("/")) { $Base = $Base + "/" }

$mirror    = Join-Path $Site "resources"
$manifest  = Join-Path $Site "resources.json"
$cachePath = Join-Path $Site ".hashcache.json"
$utf8      = New-Object System.Text.UTF8Encoding $false

# ---------------------------------------------------------------- select
# TOP LEVEL ONLY. The library reads one flat folder - ResourceManager.Init
# is called with the same root seven times, once per resource kind - so
# nothing below the root is ever opened by the game, and the folder has
# grown subdirectories that are not resources at all ("Claude outputs",
# "dr_maps"). The extension whitelist is the other half of the same
# idea: only what the game loads goes to the phone. Left behind, and why:
#   .import   Godot editor sidecars; the editor regenerates them and the
#             exported game never reads them.
#   .dll      Windows client modules that landed beside the data; a
#             phone cannot load them and should not be sent them.
#   x.roo~    editor backups. Their extension is ".roo~", not ".roo", so
#             the whitelist drops them without a special case.
#   no ext    junk. A 38 MB temp file called zidUkPjp is in the folder
#             today; it would have been the single largest download.
#   .png      NOT game data - the two in the folder are a logo and a
#             snow sprite the client never opens (its own art is under
#             res://art and Resources/). And an image type is the one
#             kind of file a hosting CDN will rewrite: the first real
#             download on a phone stopped at humanzoo-logox256.png with
#             "the server sent 28006 bytes, not 28262" - Hostinger's
#             image optimization had recompressed it on the way out.
#             .bgf, .roo and .ogg are opaque to a CDN and arrive
#             untouched. No image types in the manifest, ever.
# Case-insensitive, because the files came from a Windows box where
# ROOM.ROO and room.roo are the same thing.
$wanted = @{}
foreach ($e in "bgf","roo","ogg","wav","mp3","rsb","bsf","xml") { $wanted["." + $e] = $true }

$selected = @(Get-ChildItem -LiteralPath $Source -File | Where-Object {
  $wanted.ContainsKey($_.Extension.ToLowerInvariant())
})

# A zero-file manifest is not an empty release, it is an instruction to
# every client to delete its resources. Nothing legitimate produces one.
if ($selected.Count -eq 0) {
  Fail "no resource files selected from $Source - refusing to write an empty manifest"
}

# Ordinal, and sorted once here, because the stamp is defined over the
# sorted order and the client must arrive at the same order from the
# same names. Sort-Object in 5.1 cannot take a comparer and would sort
# by the current culture, where "a" and "B" change places.
$names = New-Object System.Collections.Generic.List[string]
$byName = @{}
foreach ($f in $selected) { $names.Add($f.Name); $byName[$f.Name] = $f }
$names.Sort([System.StringComparer]::Ordinal)

# ----------------------------------------------------------------- cache
# 467 MB takes real time to hash, and a rebuild changes a handful of
# files, so hashes are remembered in <Site>\.hashcache.json keyed by
# name. An entry is reused only when the file's size AND its last-write
# time (UTC ticks) both match what was recorded; either one differing,
# or a missing or malformed entry, rehashes the file. That is the same
# rule the mirror and FileZilla use, so the three stay in agreement.
# A file edited in place with its mtime deliberately preserved would
# fool all three alike - delete the cache file to force a full rehash.
# The cache is a cache: if it cannot be read it is simply rebuilt.
$cache = @{}
if (Test-Path -LiteralPath $cachePath) {
  try {
    $raw = [System.IO.File]::ReadAllText($cachePath, $utf8).TrimStart([char]0xFEFF)
    $obj = ConvertFrom-Json $raw
    foreach ($p in $obj.PSObject.Properties) {
      $v = $p.Value
      if ($null -ne $v -and $null -ne $v.s -and $null -ne $v.t -and $v.h -match '^[0-9a-f]{40}$') {
        $cache[$p.Name] = @{ s = [int64]$v.s; t = [int64]$v.t; h = [string]$v.h }
      }
    }
  } catch {
    Write-Warning "hash cache unreadable, rehashing everything: $($_.Exception.Message)"
    $cache = @{}
  }
}

function Get-Sha1Hex([string]$path) {
  $sha = [System.Security.Cryptography.SHA1]::Create()
  $fs = [System.IO.File]::OpenRead($path)
  try {
    $bytes = $sha.ComputeHash($fs)
  } finally { $fs.Dispose(); $sha.Dispose() }
  return ([System.BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
}

$hashedFresh = 0
$hashedCached = 0
$newCache = @{}
$entries = New-Object System.Collections.Generic.List[object]
foreach ($n in $names) {
  $f = $byName[$n]
  $size = [int64]$f.Length
  $ticks = [int64]$f.LastWriteTimeUtc.Ticks
  $h = $null
  if ($cache.ContainsKey($n)) {
    $c = $cache[$n]
    if ($c.s -eq $size -and $c.t -eq $ticks) { $h = $c.h; $hashedCached++ }
  }
  if ($null -eq $h) { $h = Get-Sha1Hex $f.FullName; $hashedFresh++ }
  $newCache[$n] = @{ s = $size; t = $ticks; h = $h }
  $entries.Add(@{ n = $n; s = $size; h = $h; f = $f })
}

# ---------------------------------------------------------------- mirror
# An EXACT mirror, because the owner selects the whole folder in
# FileZilla and drags it up. Anything left lying in <Site>\resources\
# goes to the site, so files that are no longer selected are deleted
# here rather than merely not copied - that is how removed resources
# leave the site and how junk never reaches it. Copy when size or mtime
# differs, and give the copy the source's mtime, because FileZilla's
# "overwrite if different size or newer" compares mtimes and a copy
# stamped with the time of copying would look newer than the server's
# file every single run and re-upload all 467 MB.
if (-not (Test-Path -LiteralPath $mirror)) { New-Item -ItemType Directory -Path $mirror | Out-Null }

$copied = 0
foreach ($e in $entries) {
  $dst = Join-Path $mirror $e.n
  $src = $e.f
  $same = $false
  if (Test-Path -LiteralPath $dst -PathType Leaf) {
    $d = Get-Item -LiteralPath $dst
    $same = ($d.Length -eq $src.Length -and $d.LastWriteTimeUtc -eq $src.LastWriteTimeUtc)
  }
  if (-not $same) {
    Copy-Item -LiteralPath $src.FullName -Destination $dst -Force
    [System.IO.File]::SetLastWriteTimeUtc($dst, $src.LastWriteTimeUtc)
    $copied++
  }
}

$deleted = 0
foreach ($stale in Get-ChildItem -LiteralPath $mirror -Force) {
  if ($stale.PSIsContainer) {
    # No subfolder belongs in the mirror; the game would never read it.
    Remove-Item -LiteralPath $stale.FullName -Recurse -Force; $deleted++
  } elseif (-not $byName.ContainsKey($stale.Name)) {
    Remove-Item -LiteralPath $stale.FullName -Force; $deleted++
  }
}

# -------------------------------------------------------------- manifest
# Built by hand with a StringBuilder rather than ConvertTo-Json. On 5.1,
# ConvertTo-Json over 4,700 objects takes tens of seconds, and its
# -Depth default of 2 silently flattens anything deeper into a string
# instead of failing - a manifest that is wrong and looks right. The
# shape here is small and fixed, so the only thing that needs care is
# the file name, which is escaped per the JSON spec (backslash, quote,
# control characters). It is written RAW otherwise: the client
# percent-encodes it when it builds the URL, and encoding it here too
# would send the phone looking for "%2520".
function Escape-Json([string]$s) {
  $sb = New-Object System.Text.StringBuilder
  foreach ($ch in $s.ToCharArray()) {
    $code = [int]$ch
    if ($ch -eq '\') { [void]$sb.Append('\\') }
    elseif ($ch -eq '"') { [void]$sb.Append('\"') }
    elseif ($code -lt 0x20) { [void]$sb.Append('\u' + $code.ToString('x4')) }
    else { [void]$sb.Append($ch) }
  }
  return $sb.ToString()
}

# The stamp is one hash over the whole listing, so the client can tell
# "nothing changed" from one string compare without walking 4,700
# entries. It is computed from the same sorted order the file is
# written in, encoded UTF-8, line per file.
$stampText = New-Object System.Text.StringBuilder
$total = [int64]0
foreach ($e in $entries) {
  [void]$stampText.Append($e.n).Append("`t").Append($e.s).Append("`t").Append($e.h).Append("`n")
  $total += $e.s
}
$sha = [System.Security.Cryptography.SHA1]::Create()
$stamp = ([System.BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($stampText.ToString()))) -replace '-', '').ToLowerInvariant()
$sha.Dispose()

$json = New-Object System.Text.StringBuilder
[void]$json.Append("{`n")
[void]$json.Append('  "stamp": "').Append($stamp).Append("`",`n")
[void]$json.Append('  "count": ').Append($entries.Count).Append(",`n")
[void]$json.Append('  "bytes": ').Append($total).Append(",`n")
[void]$json.Append('  "base": "').Append((Escape-Json ($Base + "resources/"))).Append("`",`n")
[void]$json.Append('  "files": [').Append("`n")
for ($i = 0; $i -lt $entries.Count; $i++) {
  $e = $entries[$i]
  [void]$json.Append('    { "n": "').Append((Escape-Json $e.n)).Append('", "s": ').Append($e.s).Append(', "h": "').Append($e.h).Append('" }')
  if ($i -lt $entries.Count - 1) { [void]$json.Append(",") }
  [void]$json.Append("`n")
}
[void]$json.Append("  ]`n}`n")

# The cache goes out the same way, for the same reasons (speed, and the
# BOM). Entries for files no longer selected are dropped with them.
$cj = New-Object System.Text.StringBuilder
[void]$cj.Append("{`n")
for ($i = 0; $i -lt $entries.Count; $i++) {
  $e = $entries[$i]
  $c = $newCache[$e.n]
  [void]$cj.Append('  "').Append((Escape-Json $e.n)).Append('": { "s": ').Append($c.s).Append(', "t": ').Append($c.t).Append(', "h": "').Append($c.h).Append('" }')
  if ($i -lt $entries.Count - 1) { [void]$cj.Append(",") }
  [void]$cj.Append("`n")
}
[void]$cj.Append("}`n")

# WITHOUT A BYTE ORDER MARK. Same lesson as latest.json: 5.1's
# Set-Content -Encoding UTF8 puts U+FEFF in front of the brace and
# Godot's JSON.parse_string returns Nil, which the client would read as
# "no manifest" every launch. WriteAllText with UTF8Encoding($false) is
# BOM-free on 5.1 and 7 alike. The manifest is written after the mirror
# is complete, so a <Site> folder is never left describing files it
# does not hold.
[System.IO.File]::WriteAllText($cachePath, $cj.ToString(), $utf8)
[System.IO.File]::WriteAllText($manifest, $json.ToString(), $utf8)

Write-Host ("resources.json: {0} files, {1} bytes, hashed {2} fresh / {3} cached, copied {4}, deleted {5}, stamp {6}" -f `
  $entries.Count, $total, $hashedFresh, $hashedCached, $copied, $deleted, $stamp)
