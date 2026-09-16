# Stable line keys

[日本語](STABLE_LINE_KEYS.ja.md)

> **Experimental.** In the Dialogue library from 1.1.0, on `main` but not yet in a release. The definitions below may still change before one.

A mod that keeps data about a line of dialogue (a translation, a bookmark, a chapter marker) needs to find that line again after a game update. Keying by the exact English text breaks on the smallest edit: the September 14, 2026 update fixed typos in 24 lines and every translation of them fell back to English. Keying by the Yarn line ID alone breaks when the developers re-tag or recreate lines. This library keys a line four ways and tries them strongest first.

## The keys

All of them are computed by `LineKey` in the Dialogue library and, with the same definitions, by `tools/linekeys.py`. None of them contain the text, so a repository that ships them ships none of the game's script.

| Key | Definition | Survives |
|---|---|---|
| Line ID | Yarn's `line:xxxxxxxx` tag, as `DialogueLine.LineId` gives it | any edit to the text |
| Hash | first 16 hex digits of SHA-256 over the UTF-8 text, exactly as the component receives it (tags included) | nothing; this is the key translation packs use today |
| Normalized hash | the hash of the normalized text: `<tags>` removed, ASCII letters lowercased, everything but letters, digits and spaces dropped, runs of whitespace collapsed to one space | edits to punctuation, capitalisation, spacing and TextMeshPro tags |
| Fingerprint | a 64-bit SimHash of the normalized text: every 3-character window is hashed with FNV-1a 64 and votes on each bit | small edits; similar texts are a few bits apart (`LineKey.Distance`) |

Yarn line IDs are tags the Yarn compiler writes into the script file once, so they stay put when the developers edit a line's text. They change only when a line is recreated or the script is re-tagged.

## Resolving a line

`LineResolver` holds the mod's records (`LineRecord`: the keys above, plus the node and speaker the line was seen with, and any payload) and answers `Resolve(line, displayedText)`:

1. **Line ID.** If a record has the line's ID, it wins. When the record's hash differs from the text on screen, the match is flagged `NeedsReview`: the text changed, so the mod's data for it may be stale.
2. **Hash.** The exact displayed text.
3. **Normalized hash.** Only when exactly one record has it; texts like "Yes." that several lines share are never guessed.
4. **Fuzzy.** Among the records of the same node (or all records when the node is unknown; none when the node has no records), the one whose fingerprint is nearest, if it is within 10 bits, is the only one that near (ties are broken by speaker, else refused), and both texts are at least 12 characters after normalization. Flagged `NeedsReview`.

Anything else returns null. The resolver would rather show nothing than the wrong line.

## What the numbers are based on

Measured on the game's 1,839 dialogue lines with the game's own data, kept local (the repository holds only the outcome):

- Across the September 14 update, 0 line IDs disappeared and 24 lines kept their ID while their text changed. Layer 1 alone would have kept all 24 translated.
- 300 random lines were edited three ways (a one-character typo, punctuation, capitalisation). Layers 3 and 4 recovered 68% / 93% / 91% of them; the rest were refused as too short or ambiguous. **No edit was matched to the wrong line.**

## For mod authors

```csharp
var resolver = new LineResolver();
// From your data file: keys the tool wrote, plus what you keep for the line.
resolver.Add(new LineRecord { LineId = "line:6046bedf", Hash = "84f325bca745e504", NormalizedHash = "…", Fingerprint = 0x…, NormalizedLength = 10, Node = "Ryan_1_intro", Speaker = "Ryan", Payload = "素晴らしい！" });

GameDialogue.LineShowing += line =>
{
    LineMatch match = resolver.Resolve(line, line.FullText);
    if (match != null)
    {
        Use((string)match.Record.Payload);
        if (match.NeedsReview) Log($"{line.LineId}: text changed, check the translation");
    }
};
```

Make records from text you saw on screen with `LineResolver.RecordFor(text, lineId, node, speaker, payload)`; it computes every key. Offline, `python tools/linekeys.py "text"` prints the same keys, and `python tools/linekeys.py --check` verifies the Python against `ci/linekey-vectors.json`, which the C# side is checked against too.

## Where it is going

- The Localization mod's packs will gain tool-written columns for the line ID, normalized hash and fingerprint next to the existing `key`, so translators' columns do not change and older packs keep working.
- A `rekey` tool will run the same four layers over a pack after a game update, rewrite the keys that moved and list the rows that need a human look.
