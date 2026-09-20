#!/usr/bin/env bash
# The image a CI job runs its check in: pulled when this repository's machines
# run, built from docker/Dockerfile when they cannot reach the package.
#
# A pull request from a fork gets a read-only token that cannot read a private
# package, and a fork has no package of its own. Building takes a minute or two
# there and the result is the same image, from the same Dockerfile, so a
# contributor's pull request is checked exactly as a maintainer's is - no
# secret, no permission, nothing to ask for.
#
# Prints the tag to run. Used by the workflows:
#
#   IMAGE=$(docker/ci-image.sh)
#   docker run --rm -v "$PWD":/work -w /work "$IMAGE" docker/checks.sh
set -u

REMOTE=${CI_IMAGE:-ghcr.io/tomxv/dragnwash-modframework-ci:latest}
LOCAL=dragnwash-modframework-ci:local

if docker pull --quiet "$REMOTE" >/dev/null 2>&1; then
    echo "$REMOTE"
    exit 0
fi

echo "Could not pull $REMOTE; building the image from docker/Dockerfile." >&2
docker build --quiet -t "$LOCAL" -f docker/Dockerfile . >&2 || exit 1
echo "$LOCAL"
