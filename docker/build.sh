#!/usr/bin/env bash
# Build the core and the libraries in the container (docker/Dockerfile).
#
#   docker compose run --rm build
#
# These compile against the game's own assemblies, which this repository never
# contains and the image never carries. They come from a local install: run
#
#   pwsh tools/copy-libs.ps1
#
# on the host first (Windows, with the game installed). The folder it fills,
# src/DragNWash.ModFramework/libs, is part of the mounted working tree, so the
# container sees it without anything being copied into the image.
#
# What this answers is the compiler's question - does the whole thing still
# build - not what to deploy. Deploy what the Windows build or the release
# workflow produced.
set -u

. "$(dirname "$0")/tree.sh"

LIBS=src/DragNWash.ModFramework/libs
NEEDED="Assembly-CSharp.dll UnityEngine.dll UnityEngine.CoreModule.dll BepInEx.dll 0Harmony.dll"

missing=""
for dll in $NEEDED; do
    [ -f "$LIBS/$dll" ] || missing="$missing $dll"
done

if [ -n "$missing" ]; then
    cat >&2 <<EOF
The game's reference assemblies are not in $LIBS:$missing

They are never committed and never go into the image. Copy them from your own
game install on the host, then run this again:

    pwsh tools/copy-libs.ps1
    pwsh tools/copy-libs.ps1 -GamePath "D:\\SteamLibrary\\steamapps\\common\\Drag'n Wash"

The checks that need no game files run without any of this:

    docker compose run --rm checks
EOF
    exit 2
fi

TREE=$(dnw_build_tree) || exit 1
[ "$TREE" = "/work" ] || echo "Building a copy of the tree at $TREE; bin/ and obj/ here are left alone."
cd "$TREE" || exit 1

failed=0
for project in src/*/*.csproj; do
    name=$(basename "$(dirname "$project")")
    printf '\n\033[1m== %s ==\033[0m\n' "$name"
    if ! dotnet build "$project" -c Release -warnaserror; then
        printf '\033[31mFAILED: %s\033[0m\n' "$name"
        failed=$((failed + 1))
    fi
done

printf '\n'
if [ "$failed" -gt 0 ]; then
    printf '\033[31m%d project(s) failed to build.\033[0m\n' "$failed"
    exit 1
fi
printf '\033[32mEvery project builds.\033[0m\n'
