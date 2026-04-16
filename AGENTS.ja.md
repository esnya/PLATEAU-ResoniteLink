# AGENTS

このファイルは、このリポジトリで Coding Agent を利用する際に読ませること。

## 対象
このリポジトリは、PLATEAU のデータセットを Resonite 向けの構築データへ変換し、ResoniteLink を通じた live scene update まで扱う .NET 10 の CLI-first インポートパイプラインを実装する。

## 作業ルール
- 英語の Markdown を正本として扱う。英語の `.md` を変更したら、対応する `.ja.md` も同じ変更で必ず更新する。
- 日本語の Markdown は翻訳補助として扱う。英語版と日本語版が衝突した場合は英語版を優先し、その後で日本語版を修正する。
- ユーザーから明示的な依頼がない限り、ランタイムと SDK の前提は .NET 10 のまま維持する。
- ビルド、パッケージ、フォーマット、Lint、Analyzer の共通方針はリポジトリルートで一元管理する。個別プロジェクトに重複設定を持ち込まない。
- Codex や同種の sandbox 制約つき WSL 環境では、検証フローの代わりに `dotnet format <sln|csproj>` の solution / project モードを使わないこと。MSBuild workspace を開く段階で `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>` で失敗することがある。`CONTRIBUTING.md` に記した repository の検証コマンド列を使い、その中の whitespace 検証には `dotnet format whitespace . --folder --verify-no-changes` を使う。
- push や pull request 更新の前には、`CONTRIBUTING.md` に記した検証コマンド列を必ず実行すること。`CONTRIBUTING.ja.md` はその翻訳である。
- `docs/` には、要件、アーキテクチャ意図、参照メモ、運用制約など、コードやテストだけでは表現しにくい内容だけを書く。
- 一時的に保持したい大きな改善計画は `.tmp/plans/` 配下に置き、untracked のまま維持すること。`docs/` 配下には置かず、active documentation から現行運用の根拠としてリンクや引用をせず、採用した current outcome だけを tracked な docs / code / tests へ昇格させること。
- 補助的な git worktree は `<repo>/.worktree/` 配下で管理し、同階層の sibling ディレクトリや `/tmp` に worktree を置かないこと。これにより一時的な worktree が既定の主ワークツリー外に散逸することを防ぐ。
- データセット、タイル、アダプターの概念を設計するときは、`PLATEAU-SDK-for-UNITY` の用語とインポート意味論に揃える。
- CLI のオーケストレーション、アプリケーションロジック、ドメインモデルをテスト可能でホスト非依存に保つため、Resonite 固有の I/O は抽象の背後に置く。
- 決定的な出力、明示的なコマンド入力、再現可能なローカル/CI の挙動を優先する。
- 振る舞いを変更したら、自動テストを追加または更新する。

## Live Send 手順
- Coding Agent が live test を行うときは [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md) に従うこと。[docs/live-testing.md](docs/live-testing.md) は operator 向けの workflow 参照であり、[docs/live-testing.ja.md](docs/live-testing.ja.md) はその翻訳補助にとどめる。
