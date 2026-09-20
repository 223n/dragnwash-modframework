# 検査をコンテナで回す

[English](DOCKER.md)

このリポジトリの検査はすべて Python 3.12 で動き、ゲームなしでビルドできる 2 つのプロジェクトには .NET SDK 8 が要ります。どのマシンにもそれを揃えてもらう代わりに、[`docker/Dockerfile`](../docker/Dockerfile) のイメージに入れました。GitHub Actions も同じイメージの中でジョブを回します。手元で通った検査が向こうでも通るのは、同じものを使っているからです。

```bash
docker compose run --rm checks     # CI が回す検査すべて
docker compose run --rm build      # 中核とライブラリ（libs/ が要ります）
docker compose run --rm shell      # 同じイメージのシェル
```

最初の 1 回だけイメージを作ります（1〜2 分）。あとはキャッシュが効きます。作業ツリーは `/work` にマウントしているので、手元で直したファイルがそのまま検査されます。作り直しも、イメージへのコピーも要りません。

## できること・できないこと

| | |
|---|---|
| **できる** | `check-repo.py`（バージョン、GUID、変更履歴、ドキュメントのリンク）、`linekeys.py --check`、`graphs.py --test`、`check-commits.py`、「ゲームのファイルがないこと」の確認、preloader パッチャーと `Install.exe` のビルド |
| **`libs/` があればできる** | `docker compose run --rm build` で中核とすべてのライブラリのビルド |
| **できない** | ゲームの実行、Steam Deck、`pack.ps1` とリリース zip（Build ワークフローが Windows で作ります）、Windows のプログラムの動作確認 — `CrashReporter.exe`、`CodeGraph.exe`、`CodeGraphStandalone.exe` はここでコンパイルは通りますが、WebView2 とウィンドウは Windows のものです |

ゲーム内での確認は今までどおりです。コンテナは道具を固定するものであって、ゲームではありません。

## ゲームのアセンブリはイメージに入りません

中核とライブラリは、ゲーム自身のアセンブリを参照してコンパイルします。それはこのリポジトリにも、イメージにも入りません。置き場所は Git が無視する `src/DragNWash.ModFramework/libs` で、手元のゲームからコピーします。

```bash
pwsh tools/copy-libs.ps1
```

このフォルダーはマウントした作業ツリーの一部なので、イメージには何も入れずにコンテナから見えます。足りないときは `docker compose run --rm build` がどれが無いかを言いますし、ほかの検査はこれが無くても動きます。

## bin/ と obj/ はそのままにします

Windows でビルドしたツリーには、すでに `bin/` と `obj/` に Windows のビルドが入っています。同じフォルダーで Linux のビルドをもう一度やると、そこにある生成済みの `AssemblyInfo.cs` を二重にコンパイルしてしまい（`Duplicate 'AssemblyTitleAttribute' attribute`）、次の Windows のビルドには Linux の復元結果が残ります。

そこで、そのフォルダーがあるときは [`docker/tree.sh`](../docker/tree.sh) がツリーをそれ抜きで `/build` にコピーし、コピーのほうをビルドします。出力はコンテナの中に置かれ、コンテナと一緒に消えます。手元のファイルは何も変わりません。CI が渡してくるような新しいチェックアウトは、そのままの場所でビルドするので、コンパイルエラーのパスはリポジトリのパスのままです。

コンテナのビルドから出てくるのは「通るかどうか」の答えであって、配るものではありません。配るのは Windows のビルドか [Build ワークフロー](../.github/workflows/build.yml) が作ったものです。

## GitHub Actions では

イメージは [ci-image.yml](../.github/workflows/ci-image.yml) が `ghcr.io/tomxv/dragnwash-modframework-ci` に作って置きます（`docker/` に変更があったときと、手動実行のとき）。[ci.yml](../.github/workflows/ci.yml) と [commit-checker.yml](../.github/workflows/commit-checker.yml) のジョブは、Python と SDK を順に入れる代わりに、そのイメージ**の中で**動きます。

2 つのワークフローは、わざと外に置いています。**Build** は `windows-latest` で非公開の参照アセンブリを使ってリリースを作るもの、**CodeQL** は自前の道具立てを持ち込むものです。

ジョブの名前（`check`、`consistency`、`preloader`、`installer`、`commits`）は変えていません。ブランチのルールセットがその名前を要求しているからです。

## うまくいかないとき

- **`docker compose` がデーモンに繋がらない。** Docker Desktop が起動していないか、Linux エンジンが動いていません。
- **手元で通るのに Actions で落ちる。** イメージを比べてください。ワークフローは `ghcr.io/tomxv/dragnwash-modframework-ci:latest` を使い、手元の `compose.yaml` は自分のツリーの Dockerfile から作ります。pull したあと `docker compose build` をすれば揃います。
- **git の履歴を読む検査が、作業ツリー（worktree）だと落ちる。** 作業ツリーの `.git` は親リポジトリのフォルダーを指すファイルで、そこはマウントしていないので、コンテナの中の git には読むものがありません（`fatal: not a git repository`）。`check-repo.py`、`check-commits.py`、ゲームファイルの確認はそこで止まります。作業ツリーではこの 3 つをホスト側で回してください。ビルドなど残りは、ふつうのチェックアウトと同じように動きます。
- **ビルドのエラーに `/build/...` と出る。** 上で説明したコピーです。ファイルはリポジトリの同じパスのものです。
- **手元には何も入りません。** イメージを消せば（`docker image rm dragnwash-modframework-ci:local`）跡形もなくなります。
