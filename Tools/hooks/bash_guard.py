"""PreToolUse guard on Bash/PowerShell: standing laws, enforced (Tier 2).

KIT COPY (Rootstock). The harness hands us the command BEFORE it runs; a
deny refuses it and feeds the reason back to the manager. GENERIC RULES
below apply to any project; PROJECT RULES is the block a receiving
project fills with the CEO's own laws - the commented examples are the
origin project's (Everwood) real rules, kept as templates.

Adding a rule = one more block here + its line in the tooling doc's
"The hooks" section.

PURPOSE: Kit-relative version of the PreToolUse guard on Bash and
  PowerShell: GENERIC RULES (no --no-verify, no plain force push, the format
  law's shell twin blocking shell writes into the hook-wiring settings file)
  plus a PROJECT RULES block of commented Everwood-derived templates for a
  receiving project to fill with its own laws.
INTENT: gives a new project the generic shell-level laws for free and a
  template for its own project rules; hand-adapted per project so
  refresh_kit.py does not overwrite it.

Search keys: bash guard, pretooluse, deny, force push, no-verify, project
rules template.
See also: _hooklib.py; HOOKS_METHOD.md (the contract + bootstrap).
"""
import re
import sys

from _hooklib import deny, read_input

# A WRITE whose target token is a settings file (settings.local.json never
# matches: "settings.json" is not a substring of it).
# a quoted path (spaces allowed) or an unquoted one with a separator; a bare
# "settings.json" counts only as a redirect target
_T = r"(?:\"[^\"|;]*settings\.json\"|'[^'|;]*settings\.json'|[\w.:~-]*[/\\][\w./\\:~-]*settings\.json\b)"
_TB = r"(?:" + _T + r"|settings\.json\b)"
SETTINGS_WRITE = re.compile(
    r"(>{1,2}\s*" + _TB + r")"
    r"|(\b(cp|copy|mv|move|tee|copy-item|move-item)\s+(-\S+\s+)*(\S+\s+)?" + _T + r")"
    r"|(\b(set-content|out-file|add-content)\b[^|;\n]*" + _T + r")"
    r"|(\bsed\s+-i\S*\s+(\S+\s+)?" + _T + r")"
    r"|(open\([^)]*settings\.json[^)]*[\"'][wa])", re.I)


def verdict(cmd):
    """The refusal reason for this command, or None (every rule in one place)."""
    low = cmd.lower()  # noqa: F841 - rules below use it

    # ---- GENERIC RULES -------------------------------------------------------
    if re.search(r"\bgit\s+commit\b", low) and "--no-verify" in low:
        return ("Never skip hooks (--no-verify); fix the failing hook instead.")
    # Scoped to the push invocation (up to the next |, & or ;) and case-
    # sensitive: a `git commit -F -` earlier in the same command must not read
    # as a force push (misfire 2026-09-06).
    for _m in re.finditer(r"\bgit\s+push\b([^|&;\n]*)", cmd):
        _seg = _m.group(1)
        if not (re.search(r"\s(--force|-f)\b", _seg) and "--force-with-lease" not in _seg):
            continue
        return ("No plain force pushes on a shared branch. Use --force-with-lease, "
             "and only with the CEO's explicit go-ahead.")

    # THE FORMAT LAW's shell twin (kit v1.19): hook wiring changes only
    # through an Edit the format guard can see - a shell WRITE whose target is a
    # settings file (redirect, Set-Content, sed -i, tee, copy/move, a python
    # open(..., "w")) is refused so no prompt can switch a guard off around it.
    # Prose that merely names the file passes (an audit note, a --does text).
    if SETTINGS_WRITE.search(cmd):
        return ("THE FORMAT LAW (Mazhron 2026-09-13): a settings file is never written from a "
                "shell - edit .claude/settings.json (or the kit's hooks/settings.json) with the "
                "Edit tool so the format guard can check the safety wiring. No prompt overrides "
                "a guard.")

    # ---- PROJECT RULES (104--Ogre; Ashton's laws) ----
    # 1. NO REWRITING net8-core's HISTORY. Six old commits are unsigned and
    #    stay that way: the rebase that would sign them desyncs Ashton's
    #    working copy, and only he can decide to pay for that
    #    (docs/systems/delivery.md).
    if re.search(r"\bgit\s+rebase\b", low) or \
            re.search(r"\bgit\s+(commit|am)\b[^|&;\n]*--amend", low) or \
            re.search(r"\bgit\s+filter-(branch|repo)\b", low):
        return ("NO HISTORY REWRITES on net8-core: Ashton's working copy fast-forwards from "
                "bundles, so a rewrite desyncs it. The six unsigned commits stay unsigned "
                "until he says otherwise (docs/systems/delivery.md). Get his explicit "
                "go-ahead first.")

    # 2. DELIVERY IS FAST-FORWARD ONLY. A merge commit here means the two
    #    copies diverged and something is about to be lost.
    for _m in re.finditer(r"\bgit\s+merge\b([^|&;\n]*)", low):
        if "--ff-only" not in _m.group(1) and "--abort" not in _m.group(1):
            return ("Delivery merges are --ff-only. A merge commit between the workspace clone "
                    "and Ashton's copy means they diverged; find out why instead "
                    "(docs/systems/delivery.md).")

    # 3. THE BUILD CHECK IS THE GREP, NOT THE TAIL. `dotnet build | tail`
    #    shows elapsed time only and once reported success over a real
    #    CS0136 for three runs (WORKFLOWS.md "Build and check the client").
    if re.search(r"\bdotnet\s+build\b", low) and re.search(r"\|\s*tail\b", low) \
            and "grep" not in low:
        return ("`dotnet build | tail` shows the elapsed time, not the errors - it has reported "
                "success over a failed build. Grep the output for 'error' instead "
                "(WORKFLOWS.md 'Build and check the client').")

    # ---- Templates kept from the kit, for laws this project has not set ----
    # 1. Release artifacts are never deleted (Everwood: "BUILD ZIPS ARE NEVER
    #    DELETED" - old zips are the 'before' side of dev-log comparisons).
    # DELETE = r"(^|[\s;|&(])(rm|del|erase|rmdir|remove-item|ri|move-item|mv|rename-item)\b"
    # if re.search(DELETE, low) and "<builds folder name, lowercased>" in low:
    #     return ("BUILD ARTIFACTS ARE NEVER DELETED: ... leave them.")
    #
    # 2. Tests run through the runner script only (the Script Rule): setting
    #    the project's test env var by hand is refused.
    # if re.search(r"(\$env:<prefix>_\w*test\s*=|(^|[\s;&])<prefix>_\w*test\s*=\s*\S|export\s+<prefix>_\w*test)", low) \
    #         and "run_tests.py" not in low:
    #     return ("THE SCRIPT RULE: tests run through tools/run_tests.py ONLY.")
    #
    # 3. Text doctrine in commit messages (Everwood: no em/en dashes - commit
    #    subjects become the public changelog).
    # if re.search(r"\bgit\s+commit\b", low) and ("—" in cmd or "–" in cmd):
    #     return ("NO em or en dashes in commit messages - use a hyphen, colon or comma.")
    #
    # 4. Engine/config files that a shell write corrupts (Everwood: PowerShell's
    #    BOM breaks Godot's .tres parser).
    # if re.search(r"(set-content|out-file|add-content)[^|;\n]*\.tres\b", low) or \
    #         re.search(r">{1,2}\s*\"?[^\s\"|;]*\.tres\b", low):
    #     return ("Write resource files with the Write/Edit tool, never a shell redirect.")

    return None


def _selftest():
    cases = [
        ("echo x > .claude/settings.json", True),
        ("cat .claude/settings.json", False),
        ("python -m json.tool .claude/settings.json", False),
        ("cp a.json .claude/settings.json", True),
        ("Set-Content .claude/settings.json -Value x", True),
        ("sed -i 's/a/b/' \"Future Project MDs/hooks/settings.json\"", True),
        ("python - <<EOF\nopen('.claude/settings.json', 'w')\nEOF", True),
        ("echo x > .claude/settings.local.json", False),
        ("python tools/purpose_audit.py --flag x --does \"the guard copies settings.json wiring; open( in prose\"", False),
        ("git push --force", True),
        ("git push --force-with-lease", False),
        ("git commit --no-verify -m x", True),
    ]
    fails = 0
    for cmd, expect in cases:
        got = verdict(cmd) is not None
        print(("PASS  " if got == expect else "FAIL  ") + repr(cmd[:60]))
        fails += got != expect
    print("bash_guard selftest: %d failed" % fails)
    return 1 if fails else 0


def main():
    if "--selftest" in sys.argv[1:]:
        sys.exit(_selftest())
    data = read_input()
    cmd = (data.get("tool_input") or {}).get("command") or ""
    if not cmd:
        return
    reason = verdict(cmd)
    if reason:
        deny(reason)


if __name__ == "__main__":
    main()
