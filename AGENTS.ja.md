# AGENTS

## 対象
このリポジトリは、PLATEAU のデータセットを Resonite 向けの構築データへ変換し、将来的には Resonite へ直接反映するアダプターまで含める .NET 10 ベースのインポートパイプラインを実装する。

## 作業ルール
- 英語の Markdown を正本として扱う。英語の `.md` を変更したら、対応する `.ja.md` も同じ変更で必ず更新する。
- ユーザーから明示的な依頼がない限り、ランタイムと SDK の前提は .NET 10 のまま維持する。
- ビルド、パッケージ、フォーマット、Lint、Analyzer の共通方針はリポジトリルートで一元管理する。個別プロジェクトに重複設定を持ち込まない。
- Codex や同種の sandbox 制約つき WSL 環境では、検証に `dotnet format <sln|csproj>` の solution / project モードを前提にしないこと。MSBuild workspace を開く段階で `System.Net.Sockets.SocketException (13): Permission denied /tmp/<guid>` で失敗することがある。その場合、whitespace 検証には `dotnet format whitespace . --folder --verify-no-changes` を使い、Analyzer / code-style ルールの検証には build 経由で `dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false` を使う。
- `docs/` には、要件、アーキテクチャ意図、参照メモ、運用制約など、コードやテストだけでは表現しにくい内容だけを書く。
- データセット、タイル、アダプターの概念を設計するときは、`PLATEAU-SDK-for-UNITY` の用語とインポート意味論に揃える。
- CLI のオーケストレーション、アプリケーションロジック、ドメインモデルをテスト可能でホスト非依存に保つため、Resonite 固有の I/O は抽象の背後に置く。
- 決定的な出力、明示的なコマンド入力、再現可能なローカル/CI の挙動を優先する。
- 振る舞いを変更したら、自動テストを追加または更新する。

## Live Send 手順
- 具体的な live send と Resonite UnitySDK `AutoDiscovery` の運用手順は [docs/live-testing.ja.md](/mnt/c/Users/esnya/Documents/PLATEAU-ResoniteLink/docs/live-testing.ja.md) を参照すること。
