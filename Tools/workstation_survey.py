"""Workstation survey: is THIS machine up to par with WORKSTATION.md?

Usage:
  python tools/workstation_survey.py            # HAVE / MISSING table + one ledger line
  python tools/workstation_survey.py --no-ledger

Prints one line per requirement from the CHECKS table (a mirror of the
"What a workstation needs" section in WORKSTATION.md - keep the two in
step), then appends a summary line to docs/history/workstation_runs.txt.
Exit 1 when any REQUIRED item is missing so run_all's check group stops.

The WORKSTATION RULE (the CEO 2026-09-06): a new machine is set up FROM
WORKSTATION.md, and whatever a machine gains (a package, a tool, a path)
is added to that document in the same batch. This script is the probe
that proves the document and the machine agree.

Portable twin: WORKSTATION_METHOD.md (kit) + reference tools copy.
See also: docs/systems/tooling.md "The workstation survey"; WORKFLOWS.md
"Bring a new workstation up to par".

PURPOSE: Checks this machine against the CHECKS table (a mirror of
  WORKSTATION.md's requirements), prints a HAVE or MISSING table, and
  appends a summary line to docs/history/workstation_runs.txt; exits 1 if a
  required item is missing.
INTENT: THE WORKSTATION RULE, the CEO 2026-09-06: a new machine is set up
  from WORKSTATION.md and whatever it gains is added to that document in the
  same batch; this script is the probe that proves the document and the
  machine agree.

Search keys: workstation survey, machine setup check, have missing table,
  requirements probe, up to par.
"""
import datetime
import glob
import os
import re
import shutil
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LEDGER = os.path.join(ROOT, "docs", "history", "workstation_runs.txt")
HOME = os.path.expanduser("~")
APPDATA = os.environ.get("APPDATA", os.path.join(HOME, "AppData", "Roaming"))
LOCALAPPDATA = os.environ.get("LOCALAPPDATA", os.path.join(HOME, "AppData", "Local"))
PF = os.environ.get("ProgramFiles", r"C:\Program Files")
PF86 = os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")


def ws_name():
    # WS1 = Owner, WS2 = Travis (the two known machines); else the user name.
    user = os.environ.get("USERNAME") or os.environ.get("USER") or "?"
    return {"ashto": "WS1"}.get(user.lower(), user)


def run(cmd):
    try:
        r = subprocess.run(cmd, capture_output=True, text=True, timeout=30,
                           shell=isinstance(cmd, str))
        out = (r.stdout + r.stderr).strip().splitlines()
        return out[0] if out else ""
    except Exception:
        return ""


def ver(cmd, pattern=r"(\d+\.\d+(?:\.\d+)?)"):
    line = run(cmd)
    m = re.search(pattern, line)
    return m.group(1) if m else ""


def first_existing(paths):
    for p in paths:
        for hit in glob.glob(p):
            if os.path.exists(hit):
                return hit
    return ""


def steam_libraries():
    libs = [os.path.join(PF86, "Steam")]
    vdf = os.path.join(PF86, "Steam", "steamapps", "libraryfolders.vdf")
    if os.path.exists(vdf):
        with open(vdf, encoding="utf-8", errors="replace") as f:
            libs += [p.replace("\\\\", "\\") for p in
                     re.findall(r'"path"\s+"([^"]+)"', f.read())]
    return libs


def pip_has(pkg):
    line = run([sys.executable, "-m", "pip", "show", pkg])
    return line.startswith("Name:")


def godot_exe():
    """The Godot 4.7 mono binary: M59_GODOT, the Linux unpack, or the Desktop."""
    cands = [os.environ.get("M59_GODOT", ""),
             "/tmp/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64",
             os.path.join(HOME, "Desktop", "Godot*", "Godot*mono*")]
    return first_existing([c for c in cands if c])


def android_sdk():
    cands = [os.environ.get("ANDROID_HOME", ""),
             os.path.join(APPDATA, "..", "Local", "Android", "Sdk"),
             os.path.join(HOME, "AppData", "Local", "Android", "Sdk")]
    return first_existing([c for c in cands if c])


# (label, required, probe -> detail string or "" when missing)
CHECKS = [
    ("Python 3.10+ (tools/, hooks)", True,
     lambda: ver([sys.executable, "--version"]) if sys.version_info >= (3, 10) else ""),
    ("pip: Pillow (frame stacking, the invariant tour)", True,
     lambda: "ok" if pip_has("Pillow") else ""),
    ("pip: openpyxl (the usage spreadsheet)", False,
     lambda: "ok" if pip_has("openpyxl") else ""),
    (".NET 8 SDK (dotnet build)", True,
     lambda: ver("dotnet --version")),
    ("Git", True,
     lambda: ver("git --version")),
    ("git user.name / user.email set", True,
     lambda: run("git config --global user.name")
             or run(["git", "-C", ROOT, "config", "user.name"]) or ""),
    ("Godot 4.7 mono (M59_GODOT or an unpacked build)", True, godot_exe),
    ("MobileClient project", True,
     lambda: first_existing([os.path.join(ROOT, "MobileClient", "MobileClient.csproj")])),
    (".claude/settings.json (the hooks)", True,
     lambda: first_existing([os.path.join(ROOT, ".claude", "settings.json")])),
    ("Android SDK (the phone export)", False, android_sdk),
    ("xvfb-run (headless SceneShot runs)", False,
     lambda: ver("xvfb-run --help", r"(\d+\.\d+)") or ("ok" if run("which xvfb-run") else "")),
    ("Rootstock kit clone (Future Project MDs)", False,
     lambda: first_existing([os.path.join(ROOT, "Future Project MDs", "UPGRADES.md")])),
    ("GitHub CLI gh", False,
     lambda: ver("gh --version")),
]


def main():
    ws = ws_name()
    missing_req, missing_opt = [], []
    print("WORKSTATION SURVEY - %s (%s)" % (ws, os.environ.get("COMPUTERNAME", "?")))
    for label, required, probe in CHECKS:
        try:
            detail = probe() or ""
        except Exception as e:  # a probe must never kill the survey
            detail = ""
        status = "HAVE   " if detail else ("MISSING" if required else "absent ")
        print("  %s  %-58s %s" % (status, label, detail if detail not in ("ok",) else ""))
        if not detail:
            (missing_req if required else missing_opt).append(label)
    n = len(CHECKS)
    have = n - len(missing_req) - len(missing_opt)
    summary = "%d/%d present | missing required: %s | optional absent: %s" % (
        have, n, ", ".join(missing_req) or "none", ", ".join(missing_opt) or "none")
    print("\n" + summary)
    if "--no-ledger" not in sys.argv:
        os.makedirs(os.path.dirname(LEDGER), exist_ok=True)
        stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M")
        with open(LEDGER, "a", encoding="utf-8") as f:
            f.write("%s | %s | %s\n" % (stamp, ws, summary))
    if missing_req:
        print("\nUP TO PAR: NO - install the missing items per WORKSTATION.md, then re-run.")
        sys.exit(1)
    print("\nUP TO PAR: YES")


if __name__ == "__main__":
    main()
