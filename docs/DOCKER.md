# The checks in a container

[日本語](DOCKER.ja.md)

Everything this repository checks runs on Python 3.12, and the two projects that
build without the game need the .NET SDK 8. Rather than asking every machine to
have exactly those, the image in [`docker/Dockerfile`](../docker/Dockerfile) has
them, and GitHub Actions runs its jobs in the same image. A check that passes on
your machine passes there, for the same reason.

```bash
docker compose run --rm checks     # every check CI runs
docker compose run --rm build      # the core and the libraries (needs libs/)
docker compose run --rm shell      # a shell in the same image
```

The first run builds the image (a minute or two); after that it is cached. The
working tree is mounted at `/work`, so a file you change on your machine is
checked as it is — there is nothing to rebuild and nothing to copy in.

## What it does and does not cover

| | |
|---|---|
| **Covered** | `check-repo.py` (versions, GUIDs, the changelog, documentation links), `linekeys.py --check`, `graphs.py --test`, `check-commits.py`, the "no game files" check, and the builds of the preloader patcher and `Install.exe` |
| **Covered with `libs/`** | the core and every library, through `docker compose run --rm build` |
| **Not covered** | running the game, the Steam Deck, `pack.ps1` and the release zip (the Build workflow makes it on Windows), the Windows programs' behaviour — `CrashReporter.exe`, `CodeGraph.exe` and `CodeGraphStandalone.exe` compile here, but WebView2 and a window are Windows' |

In-game testing stays where it was. A container is a fixed set of tools, not a
game.

## The game's assemblies are not in the image

The core and the libraries compile against the game's own assemblies, which this
repository never contains and the image never carries. They live in the ignored
`src/DragNWash.ModFramework/libs`, filled from a local install:

```bash
pwsh tools/copy-libs.ps1
```

That folder is part of the mounted working tree, so the container sees it
without anything being copied into the image. `docker compose run --rm build`
says which assemblies are missing when they are not there, and every other check
runs without them.

## bin/ and obj/ are left alone

A tree that has been built on Windows already has `bin/` and `obj/` with a
Windows build in them. Building the same folders a second time on Linux makes
the SDK compile the generated `AssemblyInfo.cs` it finds there a second time
(`Duplicate 'AssemblyTitleAttribute' attribute`), and leaves a Linux restore
behind for the next Windows build.

So when those folders exist, [`docker/tree.sh`](../docker/tree.sh) copies the
tree to `/build` without them and builds the copy. The output lands inside the
container and goes with it; nothing on your machine changes. A fresh checkout —
what CI hands the container — is built where it is, so every compiler error
keeps the path the repository uses.

What comes out of a container build is an answer, not a release: deploy what the
Windows build or the [Build workflow](../.github/workflows/build.yml) produced.

## In GitHub Actions

The image is built and pushed to `ghcr.io/tomxv/dragnwash-modframework-ci` by
[ci-image.yml](../.github/workflows/ci-image.yml), which runs when anything in
`docker/` changes (and by hand). The jobs in
[ci.yml](../.github/workflows/ci.yml) and
[commit-checker.yml](../.github/workflows/commit-checker.yml) then run `in` that
image instead of installing Python and the SDK step by step.

Two workflows stay outside it on purpose: **Build**, which packs the release on
`windows-latest` with the private reference assemblies, and **CodeQL**, which
brings its own toolchain.

Each job keeps its name (`check`, `consistency`, `preloader`, `installer`,
`commits`), because the branch ruleset requires those names.

## When something looks wrong

- **`docker compose` cannot reach the daemon.** Docker Desktop is not running,
  or its Linux engine is not started.
- **A check passes here and fails in Actions.** Compare the image: the workflow
  pins `ghcr.io/tomxv/dragnwash-modframework-ci:latest`, and your `compose.yaml`
  builds the Dockerfile as it is in your tree. `docker compose build` after
  pulling brings the two back together.
- **The checks that read the history fail in a git worktree.** In a worktree
  `.git` is a file pointing into the main repository's folder, which is not
  mounted, so git inside the container has nothing to read (`fatal: not a git
  repository`) and `check-repo.py`, `check-commits.py` and the no-game-files
  check stop there. Run those four on the host in a worktree; the builds and
  the rest work in the container as they do anywhere.
- **A build error names `/build/...`.** That is the copy explained above; the
  file is the same one at that path in the repository.
- **Nothing is ever installed on your machine by this.** Removing the image
  (`docker image rm dragnwash-modframework-ci:local`) removes every trace of it.
