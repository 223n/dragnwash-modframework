# Contributing

[日本語](CONTRIBUTING.ja.md)

Thanks for taking a look. This is a prerequisite mod, so other people's mods
are built on top of it. The question for any change is "does this still hold up
when five mods use it at once?", and a small, boring pull request is usually the
best kind.

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md).
Security problems don't go in a pull request or a public issue; please send
them the way [SECURITY.md](SECURITY.md) describes.

## Ways to help that are not code

- **Report what broke.** A bug report with `BepInEx/LogOutput.log`, the game
  build and the platform is worth a lot, especially right after a game update.
  - A new issue gets labels automatically (kind, area, severity), and a bug
    report that's missing a version, steps or a log gets one comment asking for
    them.
  - To do that, the title and the body's own words (leaving out code blocks,
    tables and images) and the error lines of a pasted log (with user names
    taken out of paths) are sent to TypeSafe AI's classification model
    ([`tools/issue-triage.py`](tools/issue-triage.py)).
  - A person still reads every issue.
- **Tell us what your mod can't do.** The framework exists because mods kept
  rebuilding the same machinery. If you're patching the game directly because
  the framework gives you no way to do something, that's the most useful issue
  you can open.
- **Fix the documentation.** The [wiki](https://github.com/TomXV/dragnwash-modframework/wiki)
  is where the docs for players and mod authors live. It's a repository of its
  own and GitHub wikis don't take pull requests, so open an issue here saying
  which page is wrong and what it should say. The design and research records
  stay here in [`docs/`](docs/).
- **Try it somewhere unusual**, like a Steam Deck, a Linux distribution other
  than SteamOS, or Windows on ARM. The results help whether it works or not,
  and you're welcome to add them to [`docs/GAME_BUILDS.md`](docs/GAME_BUILDS.md).
- **Sponsor it**, if you want to and can. It's never required, and nothing is
  kept behind it: https://github.com/sponsors/TomXV

## Before you start on something big

Open an issue first if you're planning a new library, a change to a public API,
or anything that changes how mods are loaded. Those are worth agreeing on
before you write them. Partly that's so your work isn't wasted, and partly it's
because [`docs/DESIGN.md`](docs/DESIGN.md) and [`docs/ROADMAP.md`](docs/ROADMAP.md)
may already have a plan for it that looks different from yours.

Small fixes don't need any of that. Just send them.

## Setting up

1. Install the .NET SDK, and BepInEx 5.4.23.5 in the game.
2. Copy the game's reference assemblies out of your own install:

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   Pass `-GamePath` if the game isn't in the default Steam library. They end up
   in `libs/` folders, which git ignores, and they **must never be committed**.
3. Build what you're working on:

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

4. Copy each plugin DLL to its own folder under `<Game>/BepInEx/plugins/<assembly name>/`,
   and `DragNWash.ModFramework.Preloader.dll` to `<Game>/BepInEx/patchers/`.

[README.md](README.md) has the same steps in more detail, and
[Playing well with others (wiki)](https://github.com/TomXV/dragnwash-modframework/wiki/Playing-well-with-others)
explains what each library is for.

## The rules

These are hard rules, and a pull request that breaks one of them can't be
merged.

- **Never commit the game's files, BepInEx binaries or anything from `libs/`.**
  A check runs on every push and pull request and fails the build if it finds
  any. That's what keeps the repository legal to publish.
- **Material from the game follows [`docs/CONTENT_POLICY.md`](docs/CONTENT_POLICY.md).**
  Anything made by hand or turned into something new is fine, but the game's
  data as it is isn't.
- **Code that touches game classes stays `internal`.** Mods see the framework's
  own types and nothing else. That boundary is the whole point, because it
  means that when the game updates, only the framework has to follow.
- **A change that breaks the public API needs a new major version** and a good
  reason, since every mod built on it will break too.

## Checks

CI runs everything that doesn't need game files, and you can run all of it
yourself:

```bash
python tools/check-repo.py        # versions, GUIDs, changelog, documentation links
python tools/linekeys.py --check  # line keys still match the vectors
python tools/check-commits.py     # no tool's attribution in the commit messages
```

With Docker you can run every one of them, plus the builds CI makes, in the
same image CI uses, so you don't need Python or a .NET SDK of your own:

```bash
docker compose run --rm checks
```

See [docs/DOCKER.md](docs/DOCKER.md).

The last one in the first list is the **Commit checker**. The history here
names the people who decided what a commit should say, and leaves out the
editor, the assistant or the IDE that typed it. The checker fails the build for
two things:

- **The message**, when it has a `Co-authored-by` line naming a tool, a
  "Generated with" footer, or a link to an assistant's session. A human
  co-author is welcome.
- **The author or the committer**, when the commit is signed by a tool's
  account, even if its message reads perfectly well. `dependabot[bot]`,
  `github-actions[bot]` and the `GitHub <noreply@github.com>` committer of a
  web merge all pass, and so does any person, whatever they're called. Claude
  is somebody's name, so the rule wants a model or a bot suffix after it before
  it refuses anything.

If it catches you, fix the commit (`git commit --amend`, adding
`--reset-author` when the author is wrong, or `git rebase -i` for an older one)
and push again.

The core and the libraries can't be built on a runner because they need the
game's assemblies, so **you** are the one who checked them. In the pull
request, say what you ran the change against: the game build, the platform, and
what you looked at.

## Versions and the changelog

Each project has its own version. If you change one, the `<Version>` in the
`.csproj`, the `Version` constant in the code and the `### <Name> <version>`
heading in [CHANGELOG.md](CHANGELOG.md) all have to agree, and that's exactly
what `tools/check-repo.py` checks. The preloader patcher follows the core's
version.

Most pull requests shouldn't bump a version at all. That usually happens when a
release is put together.

## Documentation

Everything in `docs/` comes in two languages, `NAME.md` and `NAME.ja.md`, and
each links to the other. Change both if you can. If you can only do one, say so
in the pull request and it'll get picked up. A missing translation is never a
reason to hold a fix back.

The wiki is a separate repository and doesn't take pull requests. If your
change leaves a wiki page wrong, say which one in the pull request.

## The pull request itself

- Branch off `dev` and open the pull request against `dev`, with one topic per
  pull request. Work collects on `dev` and then goes to `main` together before
  a release.
- Write the title and body so that someone reading the history a year from now
  knows what changed and why. The template asks the questions for you.
- You can write the body in both languages, but nobody expects it. Either one
  is fine.
- Draft pull requests are welcome too, including for a design you'd like to
  argue about before you finish it.

By sending a pull request, you agree that your contribution is licensed under
the [MIT license](LICENSE), like the rest of the code.

## One more thing

This is an unofficial fan project and isn't affiliated with Gator Dragon Games.
Please don't send the game's developers bugs that only happen with mods
installed, and please don't ask them to support anything here.
