#!/usr/bin/env bash
# Where the container builds.
#
# A fresh checkout - which is what CI hands the container - is built where it
# is, so every path in a compiler error is the path in the repository and the
# error lands on the right line of the right file.
#
# A working tree that has been built on the host is another matter: its bin/
# and obj/ hold a Windows build. Building in place would make the SDK compile
# the generated AssemblyInfo.cs it finds there a second time ("Duplicate
# 'AssemblyTitleAttribute' attribute"), and would leave a Linux restore behind
# for the next Windows build to trip over. So the tree is copied without those
# folders and the copy is built; the host's folders are left as they are.
#
# Sourced, not run: dnw_build_tree prints the folder to build in.
set -u

WORK=${WORK:-/work}
COPY=${COPY:-/build}

dnw_build_tree() {
    if [ -z "$(find "$WORK" -maxdepth 3 -type d \( -name obj -o -name bin \) -print -quit)" ]; then
        echo "$WORK"
        return 0
    fi
    rm -rf "$COPY"
    mkdir -p "$COPY"
    # libs/ comes with it: the game's reference assemblies are in the mounted
    # tree, never in the image.
    if ! rsync -a \
        --exclude='.git/' \
        --exclude='bin/' \
        --exclude='obj/' \
        --exclude='release/' \
        "$WORK/" "$COPY/" >&2; then
        echo "Could not copy the working tree to $COPY." >&2
        return 1
    fi
    echo "$COPY"
}
