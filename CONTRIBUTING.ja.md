# Contributing

Pull request は歓迎します。

このリポジトリは best-effort で保守しているため、review や follow-up には時間がかかることがあります。小さく焦点の絞れた変更の方が、確認しやすく取り込みやすいです。

PR を出す前に、次を確認してください。

- このリポジトリで Coding Agent を使う場合は、[AGENTS.ja.md](AGENTS.ja.md) を読ませてください。
- 明確な理由がない限り、runtime と SDK の前提は .NET 10 のまま保つ。
- English Markdown を変更した場合は、対応する `.ja.md` も更新する。
- 挙動が変わる場合は、test を追加または更新する。
- 補助的な git worktree はリポジトリ直下の `.worktree/` 配下（例: `.../<repo>/.worktree/<name>`）で運用し、隣接ディレクトリや `/tmp` の worktree を作らないこと。
- 概念 ownership を grep ベースの architecture test / naming test で縛らないこと。project reference による境界、review checklist、挙動仕様 test を優先する。

GitHub Releases を changelog の正本とします。release tag は `vX.Y.Z` 形式で作成し、各 tag で framework-dependent の CLI zip asset を公開しつつ、merge 済み pull request から release notes を自動生成します。

release label は必須ではありませんが、付けておくと生成ノートが読みやすくなります。

- `feature`, `enhancement`: user-facing な機能追加
- `fix`, `bug`, `bugfix`: 挙動修正
- `documentation`, `docs`, `meta`, `chore`: docs と保守作業
- `test`, `tests`, `internal`, `refactor`, `ci`, `build`: 内部 test / infrastructure / refactor
- `skip-release-notes`: changelog から外したい PR

label がない PR は、生成される release notes の catch-all である `Other changes` section に入ります。

正本となる検証コマンド列は次です。

```bash
dotnet restore PlateauResoniteLink.sln --locked-mode --disable-build-servers
dotnet format whitespace . --folder --verify-no-changes
dotnet build PlateauResoniteLink.sln --configuration Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

`.agents/skills/` 配下の skill-owned test は、意図的に `PlateauResoniteLink.sln` の外で運用します。
`.agents/skills/resonite-live-send-debug/tools/session-tool.cs` またはその skill-local test contract を変更した場合は、push や PR update の前に次も追加で実行してください。

```bash
dotnet restore .agents/skills/resonite-live-send-debug/tools/tests/ResoniteLiveSendDebug.ToolTests.csproj --disable-build-servers
dotnet test .agents/skills/resonite-live-send-debug/tools/tests/ResoniteLiveSendDebug.ToolTests.csproj --no-restore --verbosity normal -m:1 --disable-build-servers -p:UseSharedCompilation=false
```

低競合の変更の間で non-slow だけを素早く回したいときは、次を使います。

```bash
dotnet test PlateauResoniteLink.sln --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:UseSharedCompilation=false --filter "Category!=Slow"
```

大きな repository-improvement plan を一時的に保持したい場合は、`.tmp/plans/` 配下に置き、untracked のまま維持してください。その領域を canonical documentation として扱わず、active docs から現行運用の案内としてリンクせず、採用した結論だけを tracked documentation と review 成果物へ反映してください。

PR の説明には、何を変えたか、なぜ変えたか、残っている limitation や follow-up work があれば書いてください。

review 時には次も確認してください。

- 概念名、directory 配置、namespace 配置が ownership に一致し、互換 alias を残していない
- project reference が意図した依存方向を保っている
- 挙動変更が grep ベースの naming / boundary test ではなく、behavior-oriented test で守られている
- naming rule や boundary rule を変えた場合に、agent guidance と reviewer guidance も更新されている

ある commit が GitHub Issue を完全に解決し、merge 時に自動 close してよい場合だけ、commit message footer に `Fixes #81` や `Closes #85` を使ってください。途中段階の cut、partial migration、follow-up 用 commit では `Refs #81` を使います。
