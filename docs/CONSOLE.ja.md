# Console

[English](CONSOLE.md)

> **実験的な機能です。** Tool window ライブラリ 1.1.0 から `main` に入っていますが、まだリリースには含まれていません。

Tool window（F1）の **Console** タブは、ログを流れてくるままに色分けして表示し、コマンドを受け付けます。Mod を作る人やデバッグする人のためのもので、遊ぶだけなら使いません。

## ログ

BepInEx が記録するすべての行が、どの Mod からでも Unity からでも、レベルと出どころ（`DragNWash.ModFramework.Assets` や `Unity Log` のようなログソース名）付きで届きます。最新 2,000 行を保持します。`BepInEx/LogOutput.log` には引き続きすべてが書かれ、Console は見せるものを選ぶだけです。

| レベル | 色 | 主な用途 |
|---|---|---|
| Fatal、Error | 赤 | クラッシュ、例外 |
| Warning | 琥珀 | 差し替えの衝突、読めないファイル、Direct3D 12 の注意 |
| Message | アクセント色 | 人に向けた結果（「3 か所に当てました」など） |
| Info | 通常 | ふつうの進行 |
| Debug | 薄い色 | 細かい追跡。初期設定では非表示 |

タブを開いていないときに Error が来ると、タブのボタンが開くまで **Console !** になります。

## 見るものを選ぶ

3 通りの方法があり、どれも同じ設定に書き込みます。設定は Tool window の設定ファイルに保存されるので、再起動しても残ります。

- **タブのボタン**：レベルごとに表示・非表示、出どころで絞り込む入力欄。
- **コマンド**：`log show debug`、`log show info off`、`log level unity info`、`log level DragNWash.ModFramework.Assets debug`、`log level default warning`、`log filter Assets`。
- **Mods 画面**：Options → Mods → Drag'n Wash ModFramework: Tool window → `[Console] Show` と `[Console] Levels`。

初期設定：Mod は Info 以上、Unity のログは Warning 以上、Debug は非表示。Error を非表示にもできますが、そのときはタブにそう表示されるので、うっかり見落とすことはありません。

## コマンド

一番下の行に打って Enter。↑↓で履歴をたどれます。最初から入っているもの：

| コマンド | 内容 |
|---|---|
| `help`、`help <名前>` | 一覧、または 1 つの説明 |
| `log <n>` | 最後の n 行 |
| `log show <level> [off]`、`log level <source|unity|default> <level>`、`log filter <text>`、`log clear` | 上を参照 |
| `mods` | 読み込まれたプラグインと、フレームワークが使えないと判断した機能 |
| `scene` | 読み込まれているシーン |
| `assets textures [filter]`、`assets replacements`、`assets apply`、`assets reload` | Assets ライブラリのもの。[ASSET_TOOL.ja.md](ASSET_TOOL.ja.md) を参照 |

コマンドが例外を投げると、持ち主の Mod 名とともに赤で表示され、それ以外は何も起きません。スクリプト言語はなく、予定もありません。コマンドは Mod が登録するものだけです。

## Mod 作者向け

```csharp
ToolWindow.AddCommand(MyGuid, "tl", "tl reload | tl find <text>", args =>
{
    if (args.Length > 0 && args[0] == "reload") { Reload(); return "Reloaded."; }
    return "tl reload | tl find <text>";
});
```

- 名前は小文字 1 語。別の Mod が先に同じ名前を登録していたら、あなたのものは `あなたのguid:name`（最後のドット以降の短い形でも可）でだけ動き、`help` には両方が並びます。
- 表示したい文字列を返します。`\n` で改行です。セーブを変えるなど、軽い気持ちで実行してはいけないものは、`--yes` 引数を求めてください。
- ほかの場所から表示するには `ConsoleLog.Print(text, LogLevel)`。ふつうの BepInEx のログは、Mod 名付きでそのまま出ます。
- コマンドの文字は ASCII にするか、表示する文字を `ToolWindow.PrepareCharacters` に通してください（窓のフォントは起動時に描画されます）。
