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
- その検証コマンド列は直列で実行すること。同じ output tree に対して `dotnet build`、`dotnet test` などを並行実行すると、compiler / testhost の競合で `artifacts/build/windows/obj/.../*.dll` がロックされ、結果が汚染される。
- `docs/` には、要件、アーキテクチャ意図、参照メモ、運用制約など、コードやテストだけでは表現しにくい内容だけを書く。
- 一時的に保持したい大きな改善計画は `.tmp/plans/` 配下に置き、untracked のまま維持すること。`docs/` 配下には置かず、active documentation から現行運用の根拠としてリンクや引用をせず、採用した current outcome だけを tracked な docs / code / tests へ昇格させること。
- 補助的な git worktree は `<repo>/.worktree/` 配下で管理し、同階層の sibling ディレクトリや `/tmp` に worktree を置かないこと。これにより一時的な worktree が既定の主ワークツリー外に散逸することを防ぐ。
- データセット、タイル、アダプターの概念を設計するときは、`PLATEAU-SDK-for-UNITY` の用語とインポート意味論に揃える。
- CLI のオーケストレーション、アプリケーションロジック、ドメインモデルをテスト可能でホスト非依存に保つため、Resonite 固有の I/O は抽象の背後に置く。
- plan、state、snapshot、policy、input、output、result には、可能な限り immutable な value type を優先する。
- 正規化、検証、ID 導出、命名、グルーピング、順序付け、予算化、plan 構築には pure transform を優先する。
- value-based な実行契約で表現できるなら、共有 mutable object に対する ordered lifecycle API は避ける。
- mutable state は transport、filesystem、network、cache、logging/progress、cancellation のような狭い boundary adapter に局所化する。
- transport と target integration は immutable-by-default とし、read-once / create-only を優先し、不可避な update は専用 adapter layer に隔離する。
- ユーザーが明示的に求めない限り、`Builder`、`Manager`、`Coordinator`、`Helper`、`Util` のような広すぎる振る舞い志向の型を新設しない。
- 決定的な出力、明示的なコマンド入力、再現可能なローカル/CI の挙動を優先する。
- 振る舞いを変更したら、自動テストを追加または更新する。
- grep ベースの architecture test や naming test を、境界規範の正本として扱わないこと。命名規則と ownership はこのファイルで管理し、依存方向は project reference で縛り、テストでは observable behavior だけを守る。
- 依存性注入は stack の中腹まで貫通させること。core、application、import、bootstrap、target、transport のコードは、`new`、static factory、fallback self-wiring で concrete default を隠さない。
- legacy 変換と static projection helper は adapter edge にだけ置くこと。core concept と neutral contract は、`ToLegacy`、`FromLegacy`、target 固有 mapper utility に依存しない。
- result model は純粋に保つこと。document/read result に bootstrap 専用・discovery 専用・connection 専用・layout 専用の state を持ち込まず、必要なら別の context / snapshot に分離する。
- 概念名を rename するときは、directory、filename、namespace、project 名、resource、docs を同じ cut で揃え、互換 alias を残さない。
- namespace は directory ownership と一致させる。ownership boundary を変える場合は、namespace とディレクトリ構成を同一切りで更新し、パスと namespace の対応関係を一貫性のある境界シグナルとして保つ。
- 最終状態のアーキテクチャでは global using を残さない。dependency は各ファイルで明示し、cross-boundary usage が機械的に読める状態を保つ。
- internal contract は target-neutral に保つ。internal model に Resonite 固有の vector semantics を持ち込まず、target 固有変換は adapter edge の converter に閉じる。
- diagnostics を進化させるときは custom logging pipeline より `ILogger<T>` と framework 統合 observability を優先し、metrics は `System.Diagnostics.Metrics` を第一級の instrumentation として扱う。

## Live Send 手順
- Coding Agent が live test を行うときは [.agents/skills/resonite-live-send-debug/SKILL.md](.agents/skills/resonite-live-send-debug/SKILL.md) に従うこと。
