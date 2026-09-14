# 設計メモ

[English](DESIGN.md)

状態: 下書き（2026 年 9 月）。ここに書いたことはまだ確定ではありません。どの部分についても、Issue で議論してください。

## なぜフレームワークを作るのか

[Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) は、ゲームに入り込むために多くの仕組みを作ってきました。ゲームの Options 画面への設定の追加、ゲームパッドや Steam Deck でも操作できるゲーム内メニュー、すべてのテキスト部品へのフック、これから表示される Yarn の台詞の把握、Direct3D 12 でクラッシュしないフォント、セーブのスナップショットなどです。その多くは翻訳に限った話ではありません。ほかの Mod も同じものを作り直すことになり、ゲームがアップデートされるたびに、それぞれが別々に壊れます。

2026 年 9 月 14 日のゲームのアップデート（ビルド `9/12/2026_a93aa21a`）が良い例です。1 回のパッチで、25 行の台詞の誤字修正、2 つの新しい設定項目、ポーズメニューの新しいボタン、新しいセーブ形式が入りました。

フレームワークは、ゲームに触るコードを 1 か所にまとめ、Mod には安定した API を提供します。

## 目標

- **ゲーム内の Mods 画面。** Minecraft Forge の Mod 一覧のように、入っているすべての Mod を、ゲーム自身のメニューから確認・設定できるようにします。[Mods 画面](#mods-画面) を参照してください
- **ゲームのアップデートを 1 か所で吸収する。** フレームワークが扱う機能については、Mod はゲームのクラスを直接使わず、フレームワークの型だけを使います。ゲームが変わったらフレームワークが追従し、Mod はそのまま動き続けます
- **壊れるときは静かに壊れる。** アップデートでパッチの対象が消えたら、その機能だけが「使えない」と報告し、理由をログに出します。ゲームやほかの機能は動き続けます
- **ゲームが動くすべての環境で安全に。** Windows（Direct3D 12 と 11）、Windows on ARM、Steam Deck / Linux。Direct3D 12 のアップロード時クラッシュ（UUM-140564）のような知見は、各 Mod ではなくフレームワークが持ちます
- **ゲームのファイルは含めない。** 翻訳 Mod と同じく、リポジトリにもリリースにも、ゲームのアセット、台本、バイナリは入れません

## 今はやらないこと

- 新しい 3D コンテンツ（独自のドラゴン、モデル）の読み込み。将来はありえますが、開発者さんの考え方次第です
- BepInEx や Harmony の置き換え。フレームワークは BepInEx 5 のプラグインで、内部で Harmony を使います
- macOS。BepInEx が macOS の Unity 6.3 で読み込めるようになるまで（NeighTools/UnityDoorstop#108）

## パッケージとバージョン

- BepInEx 5 のプラグイン。GUID は `com.tomxv.dragnwash.modframework`、アセンブリは `DragNWash.ModFramework.dll`、名前空間は `DragNWash.ModFramework`
- Mod は `[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]` で依存します。最低バージョンの指定も可能です
- セマンティックバージョニング。API を固めている間は **0.x** で、マイナーバージョンでも API が変わることがあり、変更点は変更履歴に書きます。翻訳 Mod がこの上で動き、API が落ち着いたら **1.0** とし、それ以降の破壊的変更はメジャーバージョンでのみ行います
- 公開 API は `DragNWash.ModFramework` 名前空間の `public` なものすべて。ゲームの型に触るものは `internal` にとどめます

## Mods 画面

プレイヤーから見たフレームワークの中心です。入っているすべての Mod を一覧できる画面で、イメージは Minecraft Forge の Mod 一覧です。ゲームの一部として自然に見えるよう、ゲーム自身の Options 画面から開きます。

### プレイヤーから見えるもの

- Options 画面の、Back・Reset to defaults・Save が並ぶボタンの列に **Mods** ボタン。Options はタイトル画面からもポーズメニューからも開けるので、Mods 画面にもどちらからでも行けます。タイトル画面とポーズメニューは、ゲームの作りのまま変えません
- Mods 画面：左に Mod の一覧、右に選んだ Mod の詳細
  - アイコン、名前、バージョン、作者、説明、Web サイト、必要なフレームワークのバージョン
  - その Mod の**設定**。ゲームの Options 画面と同じ見た目の行で表示
  - 読み込みに失敗した Mod や、このゲームのビルドで使えない機能があればその通知
  - Mod ごとの**オン・オフの切り替え**。次回の起動から反映（下記参照）
- **Back**（ボタン、Esc、パッドのキャンセル）で Options 画面に戻る
- ゲームのほかのメニューと同じく、マウス、ゲームパッド、Steam Deck で操作できる

一覧には、フレームワークを使っていない BepInEx プラグインも表示します（GUID、名前、バージョンは BepInEx から取れます）。フレームワークを使う Mod は、説明、作者、Web サイト、アイコン、設定を追加できます。

### ゲームへの組み込み方

ゲームのメニューは、ゲームのファイルに触らずに拡張できるほど単純な作りです（ビルド `9/12/2026_a93aa21a` で確認）。

- 各画面は `Menu` コンポーネントで、GameObject の名前で `MenuManager` に登録されています。`Menu_Main`（タイトル画面。レベル内ではポーズメニュー）、`Menu_Options`、`Menu_SlotsLoad` などです
- `Menu_Options` の構成は `Container/Panel` の下に、`TitleImage`、設定ライブラリが行を生成する `Scroll View`、`Back`・`ResetToDefaults`・`Save` が入った `LeftButtons` です。タイトル画面でもレベル内でも同じ構成です
- ボタンは自分の GameObject 名を「意図」として送り（`MenuWithButtons`）、表示中のメニューが、別のメニューへの名前指定の遷移を返します。例：`new MenuResponseTransition("Menu_Options", ...)`
- なので、フレームワークは次のようにできます
  1. `Menu_Options/Container/Panel/LeftButtons` のボタンを複製し、名前を `Mods` にして、独自のラベルを付ける
  2. `MenuOptions.OnEvent` にパッチを当て、`Mods` の意図で `Menu_Mods` に遷移させる
  3. Options 画面のレイアウトを元に `Menu` の派生クラスとして `Menu_Mods` を作り、`MenuManager` に登録する。`Back` では `Menu_Options` に戻す
- ゲーム内で確認すること：Options 画面で保存前に変えた設定が、Mods 画面に行って戻ってきたときに、Back のように元に戻されず残っているか
- ゲームのボタンは文字が絵に描き込まれています。Mods ボタンと Mods 画面の文字はすべて、ゲーム自身の TMP フォントを使った TextMeshPro のテキストにします。絵を描いたりコピーしたりせずに済み、どのラベルも翻訳できます（翻訳 Mod のテキストフックが、ほかの UI テキストと同じように拾います）
- Mod がアイコンを用意している場合は起動時に読み込むので、Direct3D 12 でも安全です

### Mod のオン・オフ

起動中のゲームから Mod を取り外すことはできないため、切り替えは次回の起動から反映し、画面にもそう表示します。

- フレームワークに、小さな BepInEx のプリローダーパッチャー（`BepInEx/patchers/DragNWash.ModFramework.Preloader.dll`）を同梱します。パッチャーはどのプラグインのアセンブリより先に動くので、その時点ではファイルがまだ使われていません
- 起動時にパッチャーがフレームワークの「オフにした Mod」の一覧を読み、その DLL の名前を `.dll.disabled` に変えます（オンに戻したら元の名前に戻します）。BepInEx からはオフの Mod が見えなくなるだけで、プレイヤーがファイル名を戻せば手作業でも元に戻せます
- フレームワーク自身は、自分の画面からはオフにできません
- ほかの Mod が依存している（`BepInDependency`）Mod をオフにするときは、依存している Mod を一覧で示し、確認してから行います

### フレームワークを使っていない Mod

Mods 画面は、フレームワークで作られた Mod だけでなく、すべての BepInEx の Mod に対応します。Mod 側に必要なことは何もなく、API は「ファイルから分かること」に情報を足すだけです。

- **名前、バージョン、依存関係**は、プラグインの `[BepInPlugin]`、`[BepInDependency]`、`[BepInIncompatibility]` 属性から取ります
- **説明と作者**は、DLL の `AssemblyDescription` と `AssemblyCompany` 属性、それに Mod のフォルダにある Thunderstore の `manifest.json`（説明、Web サイト）から取ります
- DLL は BepInEx 自身が同梱している Mono.Cecil で読むので、Mod のコードは実行されず、ゲームに余計なものも読み込まれません
- **読み込まれなかったプラグイン**も一覧に出し、分かる場合は理由を示します（入っていない依存先、一緒に使えない Mod）。分からない場合は `BepInEx/LogOutput.log` を案内します
- `BepInEx/patchers` の**プリローダーパッチャー**も一覧に出しますが、画面からはオフにできません
- `ModFramework.Register(ModInfo)` で登録した情報は、ファイルから読んだ情報より優先します

### Mod を作る方へ

```csharp
ModFramework.Register(new ModInfo
{
    Guid = MyPlugin.Guid,
    Description = "Adds ...",
    Authors = new[] { "Me" },
    Website = "https://github.com/me/mymod",
});
```

Mods 画面に出る設定は、その Mod の BepInEx の設定項目から作ります（`ConfigEntry<bool>` はトグル、範囲付きの数値はスライダー、enum はドロップダウン）。UI を書かなくても設定ページができます。Settings API を使えば、ゲーム自身の Options 画面に行を足すこともできます。

## API の候補

ほとんどは翻訳 Mod で実際に動いているコードが元です（ファイル名は翻訳 Mod の `src/DragNWashLocalization/` のもの）。

| 分野 | Mod が使えるもの | 元になるコード |
|---|---|---|
| Mods 画面 | Options 画面の Mods ボタン、Mod の一覧と詳細、次回起動から反映するオン・オフ、BepInEx の設定から作る設定ページ、`ModFramework.Register(ModInfo)` | 新規（`OptionsLanguage.cs` で得たメニューの知見を使う） |
| ゲーム情報 | Unity のバージョン、グラフィックス API、プラットフォーム、ゲームのビルド、「動作確認済みのビルドか」 | `Plugin.cs` の起動時チェック |
| 設定 | ゲームの Options 画面に行（ドロップダウン、トグル、スライダー）を追加。変更するとプレビューされ、ゲームの Save で保存、Back で元に戻る | `OptionsLanguage.cs` |
| ツールウィンドウ | 開発・デバッグ用の共有ウィンドウ（既定は F1）に各 Mod がタブを登録。カーソル解放、ウィンドウの裏への入力の遮断、ゲームパッドと Steam Deck のトラックパッドでのクリック、CJK 対応のメニュー用フォント | `Plugin.ImGui.cs`、`CursorUnlock.cs`、`InputBlocker.cs`、`VirtualClick.cs`、`MenuFontBundle.cs` |
| テキスト | TMP のテキストが表示される前のイベント（元の文字列と部品付き）で、Mod が差し替えられる。必要なときに再適用（言語切り替え後など） | `TmpTextPatches.cs` |
| 会話 | 台詞が表示される直前、選択肢が出る直前のイベント（台詞 ID、話者、ノード付き）。読み込まれた Yarn プロジェクト | `LineIdContext.cs`、`SpeakerLookup.cs`、`DialogueDumper.cs` |
| フラグとセーブ | ゲームのフラグの読み取り。Mod が何かを変える前のセーブスロットのスナップショット | `FlagCatalog.cs`、`SaveHistory.cs` |
| アセット | フォント、テクスチャ、アセットバンドルを安全なタイミングで読み込む（Direct3D 12 では起動時） | `FontFallback.cs`、`MenuFontBundle.cs` |
| インストーラー | BepInEx、フレームワーク、選んだ Mod をまとめて入れるインストーラー（Windows、Steam Deck） | `installer/` |

## 作業の順番

1. **0.1 骨組み。** プラグイン、`ModFramework`、`GameInfo`、ビルドとリポジトリのルール（このバージョン）
2. **0.2 Mods 画面。** Options 画面の Mods ボタン、入っている Mod の一覧と詳細、`ModInfo`、プリローダーパッチャーによる Mod のオン・オフ
3. **0.3 設定。** BepInEx の設定から作る Mods 画面の設定ページと、ゲームの Options 画面に行を足す API。最初の利用者は翻訳 Mod の言語設定
4. **0.4 ツールウィンドウ。** 開発ツール用の共有 F1 ウィンドウ、カーソルと入力の扱い
5. **0.5 テキスト。** テキストのイベント
6. **0.6 会話。** 台詞と選択肢のイベント
7. **アセット、フラグとセーブ、インストーラー**を、翻訳 Mod が必要とする順に
8. Drag'n Wash Localization v1.0.0 がフレームワークの上で動いたら **1.0**

各ステップで翻訳 Mod から機能を 1 つ移し、次のステップに進む前に、翻訳 Mod のその機能をフレームワーク経由に切り替えます。どのステップも、Windows と Steam Deck の実機でテストします。

## ゲームのアップデートへの追従

- フレームワークを確認したゲームのビルドの一覧を持つ
- パッチ対象ごとに、起動時に存在を確認し、なければ分かりやすいログを出す
- アップデート後は、ゲームのアセンブリを逆コンパイルして差分を取り、Yarn の文字列テーブルの差分を取り、一覧を更新してリリースする

## 未決定のこと

- 複数の Mod がタブを登録したときの、ツールウィンドウでの見せ方（順番、名前）
- インストーラーをこのリポジトリに置くか、翻訳 Mod のリポジトリに残すか
- GitHub Releases 以外での配布
- 開発者さんの Mod への考え方。翻訳よりもフレームワークのほうが、影響が大きいです
