# Drag'n Wash ModFramework

[English](README.md)

[Drag'n Wash](https://store.steampowered.com/app/4739660/) 用の前提 Mod（BepInEx 5）です。ゲームに入り込むためのコードを 1 か所にまとめ、ほかの Mod に安定した API として提供します。ゲームの Options 画面への設定の追加、ゲーム内の Mod メニュー、テキストや会話のイベント、Direct3D 12 で安全なアセットの読み込みなどです。ゲームがアップデートされても、追従が必要なのはフレームワークだけになります。

> [!WARNING]
> **開発初期です。** バージョン 0.1 は骨組みだけで、プレイヤーが入れるものはまだありません。1.0 までは API が変わります。

最初にこの上で動く Mod は、[Drag'n Wash Localization](https://github.com/TomXV/dragnwash-localization) の v1.0.0 になる予定です。

目標、予定している API、作業の順番は [docs/DESIGN.ja.md](docs/DESIGN.ja.md) を参照してください。

## Mod を作る方へ

`DragNWash.ModFramework.dll` を参照し、BepInEx がフレームワークを先に読み込むよう依存関係を宣言します。

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency(ModFramework.Guid, BepInDependency.DependencyFlags.HardDependency)]
public class MyMod : BaseUnityPlugin
{
    private void Awake()
    {
        ModFramework.Ready += () =>
        {
            if (GameInfo.IsDirect3D12)
            {
                // フォントやテクスチャは、後回しにせずここで読み込む
            }
        };
    }
}
```

## ビルド

1. .NET SDK を入れ、ゲームに BepInEx 5.4.23.5 を入れておく
2. 自分のゲームから参照アセンブリをコピーする（コミットはしません）

   ```bash
   pwsh tools/copy-libs.ps1
   ```

   ゲームが既定の Steam ライブラリにない場合は `-GamePath` を指定します
3. ビルドする

   ```bash
   dotnet build src/DragNWash.ModFramework/DragNWash.ModFramework.csproj -c Release
   ```

DLL は `src/DragNWash.ModFramework/bin/Release/` にできます。試すときは `<ゲーム>/BepInEx/plugins/DragNWash.ModFramework/` にコピーしてください。

## このリポジトリのルール

- ゲームのファイル、BepInEx のバイナリ、`libs/` の中身は絶対にコミットしません。プッシュとプルリクエストのたびに自動でチェックします
- ゲームのクラスに触るコードは `internal` にとどめ、Mod にはフレームワーク自身の型だけを見せます

## 開発者の方へ

本プロジェクトは非公式のファン制作物で、Gator Dragon Games とは無関係です。ゲームのアセットやコードは含まず、ゲームのファイルを書き換えることもありません（BepInEx が実行時に読み込みます）。開発チームの方で懸念がある場合は、このリポジトリの Issue かメンテナーへの連絡でお知らせください。ご希望に応じて修正または公開停止します。

## ライセンス

[MIT](LICENSE)
