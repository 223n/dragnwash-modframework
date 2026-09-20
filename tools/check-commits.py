"""Commit checker: no tool's attribution in the history, run by CI on every push and pull request.

This repository's history is written in one voice. A commit message must not
credit the editor, the assistant or the IDE that happened to type it - only the
people who decided what it should say. A human co-author is fine; a tool is not.

    python tools/check-commits.py                 # what is not yet on origin/main
    python tools/check-commits.py <base>..<head>  # an explicit range (CI passes this)
    python tools/check-commits.py <base> <head>   # the same, as two arguments

A failure's reason is an annotation on the run. To fix one before it is pushed:

    git commit --amend            # the last commit
    git rebase -i <base>          # an older one, then reword it
"""
import re
import subprocess
import sys

# Attribution that must not appear in a commit message. Each entry is a name for
# the report and a pattern matched against the whole message, case-insensitively.
# A co-author who is a person is welcome; these are tools.
#
# The link rules are deliberately narrow. claude.ai is where a session lives, so
# any address there is one of those links and none of them belongs in a commit
# message. A documentation address such as docs.anthropic.com is left alone: the
# Bridge answers AI clients over MCP, and a commit citing the specification it
# follows is a normal commit. The @anthropic.com rule needs the at sign, so it
# catches the address in a trailer and not a link to a page.
FORBIDDEN = [
    ("Claude's attribution", r"co-?authored-by:.*\b(claude|anthropic)\b"),
    ("an Anthropic address", r"@anthropic\.com"),
    ("Claude Code's footer", r"generated with \[?claude code"),
    ("an assistant's footer", r"\N{ROBOT FACE}\s*generated with"),
    ("a Claude session link", r"claude\.ai/"),
    ("a Claude Code link", r"claude\.com/claude-code"),
    ("Copilot's attribution", r"co-?authored-by:.*\bcopilot\b"),
    ("Cursor's attribution", r"co-?authored-by:.*\bcursor(\s|@|>)"),
    ("Codex's attribution", r"co-?authored-by:.*\bcodex\b"),
    ("Devin's attribution", r"co-?authored-by:.*\bdevin\b"),
    ("Aider's attribution", r"co-?authored-by:.*\baider\b"),
]

SEPARATOR = "\x1e"  # a record separator no commit message contains


def git(*args):
    done = subprocess.run(["git", *args], capture_output=True, text=True, encoding="utf-8")
    if done.returncode != 0:
        return None
    return done.stdout


def exists(ref):
    return git("rev-parse", "--verify", "--quiet", ref + "^{commit}") is not None


def default_range():
    """Everything on this branch that origin/main does not have yet."""
    for base in ("origin/main", "main"):
        if exists(base) and git("merge-base", base, "HEAD") is not None:
            return base + "..HEAD"
    return "HEAD~1..HEAD" if exists("HEAD~1") else "HEAD"


def commits(rev_range):
    out = git("log", "--format=%H%x1e%B%x1e", rev_range)
    if out is None:
        return None
    records = out.split(SEPARATOR)
    return [(records[i].strip(), records[i + 1]) for i in range(0, len(records) - 1, 2)]


def main(argv):
    if len(argv) == 2:
        rev_range = argv[0] + ".." + argv[1]
    elif len(argv) == 1:
        rev_range = argv[0]
    elif not argv:
        rev_range = default_range()
    else:
        print("usage: check-commits.py [<base>..<head> | <base> <head>]")
        return 2

    found = commits(rev_range)
    if found is None:
        # A range CI cannot resolve (a new branch, or a force-push that left
        # github.event.before behind) is not a reason to fail the build.
        print(f"Nothing to check: {rev_range} is not a range this clone can resolve.")
        return 0

    errors = []
    for sha, message in found:
        subject = message.strip().split("\n")[0][:72]
        for who, pattern in FORBIDDEN:
            if re.search(pattern, message, re.I):
                errors.append(
                    f"{sha[:7]} carries {who} in its commit message: \"{subject}\". "
                    f"This history names only the people who decided what the commit should say. "
                    f"Reword it (git commit --amend, or git rebase -i) and push again."
                )
                break

    if errors:
        for e in errors:
            print(f"::error::{e}")
        print(f"{len(errors)} commit(s) with a tool's attribution.")
        return 1

    print(f"OK: {len(found)} commit(s) checked, no tool's attribution ({rev_range}).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
