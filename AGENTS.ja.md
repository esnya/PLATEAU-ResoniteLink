# AGENTS

このファイルは、このリポジトリで Coding Agent を利用する際に読ませること。

## 対象
このリポジトリは、PLATEAU のデータセットを Resonite 向けの構築データへ変換し、ResoniteLink を通じた live scene update まで扱う .NET 10 の CLI-first インポートパイプラインを実装する。

## 作業ルール
- 英語の Markdown を正本として扱う。英語の `.md` を変更したら、対応する `.ja.md` も同じ変更で必ず更新する。
- ユーザーから明示的な依頼がない限り、ランタイムと SDK の前提は .NET 10 のまま維持する。
- ビルド、パッケージ、フォーマット、Lint、Analyzer の共通方針はリポジトリルートで一元管理する。個別プロジェクトに重複設定を持ち込まない。
- Codex や同種の sandbox 制約つき WSL 環境では、検証フローの代わりに `dotnet format <sln|csproj>` の solution / project モードを使わないこと。MSBuild workspace を開く段階で `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>` で失敗することがある。repository 標準の入口は `bash scripts/verify-ci.sh` であり、その中の whitespace 検証は `dotnet format whitespace . --folder --verify-no-changes` を使う。
- push や pull request 更新の前には、必ず `bash scripts/verify-ci.sh` を実行すること。contributor 向けの検証フロー正本は `CONTRIBUTING.ja.md` であり、script の内部コマンド列を他文書に再定義したり順序を並べ替えたりしないこと。
- `docs/` には、要件、アーキテクチャ意図、参照メモ、運用制約など、コードやテストだけでは表現しにくい内容だけを書く。
- 一時的に保持したい大きな改善計画は `.tmp/plans/` 配下に置き、untracked のまま維持すること。`docs/` 配下には置かず、active documentation から現行運用の根拠としてリンクや引用をせず、採用した current outcome だけを tracked な docs / code / tests へ昇格させること。
- 補助的な git worktree は `<repo>/.worktree/` 配下で管理し、同階層の sibling ディレクトリや `/tmp` に worktree を置かないこと。これにより一時的な worktree が既定の主ワークツリー外に散逸することを防ぐ。
- データセット、タイル、アダプターの概念を設計するときは、`PLATEAU-SDK-for-UNITY` の用語とインポート意味論に揃える。
- CLI のオーケストレーション、アプリケーションロジック、ドメインモデルをテスト可能でホスト非依存に保つため、Resonite 固有の I/O は抽象の背後に置く。
- 決定的な出力、明示的なコマンド入力、再現可能なローカル/CI の挙動を優先する。
- 振る舞いを変更したら、自動テストを追加または更新する。

## Live Send 手順
- 具体的な live send と Resonite UnitySDK `AutoDiscovery` の運用手順は [docs/live-testing.ja.md](docs/live-testing.ja.md) を参照すること。
