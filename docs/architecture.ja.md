# Architecture

## レイヤー

- `src/Plateau.ResoniteLink.Domain`
  PLATEAU 入力と Resonite 向け mesh / material payload の正規化されたモデルを置く。
- `src/Plateau.ResoniteLink.Application`
  入力検証、CityGML からの payload 生成、Resonite 側アダプター呼び出しのオーケストレーションを置く。
- `src/Plateau.ResoniteLink.Cli`
  コマンドライン構文、入出力、現時点の ResoniteLink live adapter を置く。
- `tests/Plateau.ResoniteLink.Tests`
  CLI の構文、要件に近いアプリケーション挙動、決定的な live payload 生成を検証する。

## 境界

- Resonite への書き込みはアプリケーション層の抽象越しに行う。CLI は現段階のアダプター実装を差し込むだけに留める。
- PLATEAU のデータセット、mesh code、ローカル/remote といった概念はドメインモデルで正規化しつつ、利用者が触る名称は可能な範囲で Unity SDK に合わせる。
- ResoniteLink の asset I/O は末端に閉じ込め、アプリケーション層は transport 固有の command ではなく mesh / material payload を渡す。
- 大きい取り込みでは、live payload 全体を先にメモリへ積まず、city object を非同期に逐次ストリームして下流アダプターへ渡す。

## 設定方針

- SDK、Analyzer、フォーマット、パッケージバージョンはルートで一元管理する。
- 各プロジェクトは個別に必要な差分だけを持つ。
- CI はローカル検証フローと同じで、以下を実行する。
  - `dotnet restore Plateau.ResoniteLink.sln --locked-mode`
  - `dotnet build-server shutdown`
  - `dotnet format whitespace . --folder --verify-no-changes`
  - `dotnet build Plateau.ResoniteLink.sln --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false`
  - `dotnet test Plateau.ResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 -p:UseSharedCompilation=false`
