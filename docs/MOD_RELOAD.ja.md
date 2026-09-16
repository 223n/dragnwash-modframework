# Mod のホットリロード（設計）

[English](MOD_RELOAD.md)

> **設計だけです。** まだ何も作っていません。議論のために experimental ブランチに置いています。Issue か、プルリクエストへのコメントでどうぞ。

Mod をビルドし、ゲームを閉じ、起動し、タイトル画面を抜け、店を開く。1 回の試しに 1 分、1 時間に何十回。Localization は*翻訳ファイル*なら再起動なしで読み直せます。このメモは *Mod そのもの*の話です。ビルドしたら、ゲームを動かしたまま、新しい DLL が動いている版と入れ替わる。開発者ツールの中の機能（GUIDE のルール 8）で、遊ぶ人が目にすることはありません。

## できること・できないこと

- **Mono はアセンブリを外せません。**「リロード」とは、新しいビルドを古いものの隣に読み込み、古いものが何もしないようにすることです。古いコードはメモリに残りますが、開発中なら 1 回あたり数百 KB で、問題になりません。BepInEx.Debug の ScriptEngine も同じ方法です。
- **Mod（プラグイン）：できます。** Mod がフレームワークに登録するものはすべて GUID を持つので、古い版をきれいに外せます。
- **ライブラリ（フレームワークの Dialogue、Assets など）：できません。** ほかの Mod がライブラリの型を直接参照しているので、ライブラリだけ新しくしても、その Mod は古い型に結びついたままです。ライブラリは再起動で、この設計では扱いません。
- **遊ぶ人向けの「再起動なしで Mod を入れる・更新する」：今はやりません。** 仕組みは同じですが、「起動時に一度だけ」を前提に書かれた Mod は壊れ、下の Direct3D 12 の問題が遊ぶ人に降りかかります。今の「再起動してください」の流れで十分です。開発者向けの機能が落ち着いてから考え直します。

## 仕組み

1. **監視。** 開発者ツールがオンの間、対応を宣言した Mod（後述）について `BepInEx/plugins/<Mod>/*.dll` を見張ります。ファイルが変わってから 0.5 秒静かになったら発火します。ビルドのコピーが終わるのを待つためです。
2. **先に新しいビルドを確かめる。** 新しい DLL をバイト列から読み込み（ファイルはロックされず、次のビルドを邪魔しません）、その `BaseUnityPlugin` を探します。失敗したら理由を Console に出し、古い版はそのまま動かします。入れ替えられないものを止めることはありません。
3. **古い版を外す。** この順で：
   - `ModFramework.UnloadOwned(guid)`：その GUID で登録されたものをすべて消します。Tool window のタブとコマンド、テキストの書き換え、`GameEvents` の処理、`ModInfo`、サービス、`GameHooks` の記録、Options の行。
   - `Harmony.UnpatchAll(guid)`：Mod 自身のパッチ。Mod が Harmony の ID に自分の GUID を使わなければならないのはこのためです。
   - プラグインの MonoBehaviour を `Destroy`。`OnDestroy` で Mod 自身の後始末をします。
   - ライブラリのフックは残ります。共有のものだからです。
4. **新しい版を始める。** フレームワーク自身の GameObject にプラグインの型を `AddComponent` し、起動時と同じように `Awake` が走ります。`Chainloader.PluginInfos` には触りません。リロードした版は BepInEx の管理外で、Mods 画面にもそう出ます（「3 回リロード済み。BepInEx が読み込んだファイルではありません」）。
5. **報告。** Console に 1 行、「<Mod> をリロードしました（3 回目）」。失敗は理由つきで赤く出ます。新しい版の `GameEvents.OnGameStarted` の処理は、遅れて登録したときと同じくすぐ走ります。

### Direct3D 12

新しい版の `Awake` はゲームの途中で走ります。そこでテクスチャを読み込んだりフォントを準備したりする Mod は、Direct3D 12 が落ちうるまさにその瞬間にアップロードすることになります（Unity UUM-140564）。テクスチャのリロードと同じ守り方をします。リロードの前に印のファイルを書き、終わったら消す。次の起動で印が残っていれば、リロードでゲームが落ちたということで、自分で戻すまでリロードはオフになります。Console は `-force-d3d11` で作業するよう勧めます。

## Mod 側の約束（GUIDE に載せる）

見張るのは、リロードできると宣言した Mod だけです。登録時に `ModInfo.Reloadable = true` にするか、プラグインのクラスに `[ReloadableMod]` 属性を付けます。何も言わない Mod はリロードされません。約束を守るには：

- Harmony のインスタンス ID は Mod の GUID（`new Harmony(MyMod.Guid)`）。`UnpatchAll(guid)` がすべてのパッチを見つけられるように。
- 自前の static フィールドで抱えず、フレームワーク経由で登録する（`AddTab`、`AddRewriter`、`GameEvents`、`Services`、`GameOptions`）。フレームワークが知らないものは外せません。
- static な状態は、新しいアセンブリの static に住むものだけがリロードで初期化されます。古い版がゲームに渡したもの（コルーチン、`DontDestroyOnLoad` のオブジェクト）は `OnDestroy` で片付けてください。
- `Awake` に、Direct3D 12 でゲームの途中に走らせて危ないことを置かない。置くなら `GameFonts.RuntimeUploadsAreSafe` で分ける。

## 使い方

- Console：手動なら `mods reload <guid>`、監視の切り替えは `mods watch on|off`。`mods` は、どの Mod がリロード対応で、それぞれ何回リロードしたかを出します。
- ビルドのたびに `BepInEx/plugins/<Mod>/` へ DLL をコピーする `.csproj` の `Target` を、コピーして使える例として GUIDE に載せます。
- 最初の対応 Mod は Localization で、ここが試験場です。`UnloadOwned` は Localization が登録するものに合わせて書き、数日使ってから設計を直します。

## フレームワーク側に先に要るもの

| 部品 | 今 | 必要なもの |
|---|---|---|
| Tool window のタブとコマンド | 登録ごとの `IDisposable` | GUID 単位でまとめて外す |
| テキストの書き換え | 登録ごとの `IDisposable` | GUID 単位でまとめて外す |
| `GameEvents` | `Remove(guid)` | 済み |
| `ModInfo`、`Services`、`GameHooks`、`GameOptions` | 外す手段なし | `UnloadOwned(guid)` で外す。サービスの利用側は、次に取りに来るまで古いインスタンスを持ったままなので、新しいものに対して `Services.WhenAvailable` のコールバックをもう一度呼ぶ |
| Dialogue の購読、Saves | 登録ごと | GUID 単位でまとめて外す |

## 順番

1. `ModFramework.UnloadOwned(guid)` と、各ライブラリの GUID 単位の解除。これだけでも役に立ちます。Mods 画面でオフにした Mod を同じ方法で外せます。
2. 読み込みと入れ替え、Console のコマンド。
3. 監視。
4. GUIDE の節と `.csproj` の例。
5. Localization が対応を宣言し、1 週間使い、設計の抜けを見つける。

この設計に含めないもの：ライブラリのリロード、遊ぶ人向けのリロード、DLL 以外のアセットのリロード（テクスチャには専用のものがあり、翻訳ファイルにもあります）。
