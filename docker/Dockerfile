# The checks and the builds that need no game files, in one image.
#
# Why: everything this repository checks - the consistency of versions, GUIDs,
# the changelog and documentation links, the line-key vectors, the graph files,
# the commit messages - runs on Python, and the two projects CI builds (the
# preloader patcher and Install.exe) need the .NET SDK. Without this image each
# machine has to have the right Python and the right SDK, and a check that
# passes on one can fail on another. With it, `docker compose run --rm checks`
# is the same run everywhere, and the same one GitHub Actions makes.
#
# Ubuntu 24.04 ("noble"), because its python3 is 3.12, which is the version the
# workflows pin. The .NET SDK is 8, as setup-dotnet installs.
#
# rsync is for docker/tree.sh, which copies a tree that has already been built
# on the host so the two builds do not write into the same bin/ and obj/.
#
# What is NOT in this image, and never will be: the game's assemblies. They are
# copied from a local install into the ignored libs/ folder (tools/copy-libs.ps1)
# and fetched by the release build from a private repository. The core and the
# libraries therefore build in this image only when libs/ is mounted with the
# rest of the working tree; docker/build.sh says so when it is not.
FROM mcr.microsoft.com/dotnet/sdk:8.0-noble

# python3 is 3.12 here; python-is-python3 makes `python tools/...` work, which
# is how every script is written in the documentation. git is for the checks
# that read the history (check-repo.py lists tracked files, check-commits.py
# walks a range); curl and unzip fetch BepInEx for the preloader build.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        python3 \
        python-is-python3 \
        git \
        curl \
        unzip \
        rsync \
        ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# The working tree is mounted from the host, so its files belong to another
# user than the one in the container. Without this, every git command inside
# refuses to run ("detected dubious ownership") and the checks that read the
# history fail for a reason that has nothing to do with the repository.
RUN git config --global --add safe.directory '*'

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# A tree that was already built on the host is copied here first and built
# there, so the host's bin/ and obj/ are left as they are (docker/tree.sh).
ENV COPY=/build

WORKDIR /work

# Nothing is copied in. The working tree is mounted at /work, so a change on
# the host is checked immediately, with no rebuild of the image.
CMD ["docker/checks.sh"]
