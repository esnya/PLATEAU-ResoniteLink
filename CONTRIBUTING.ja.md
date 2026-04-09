# Contributing

Pull request は歓迎します。

このリポジトリは best-effort で保守しているため、review や follow-up には時間がかかることがあります。小さく焦点の絞れた変更の方が、確認しやすく取り込みやすいです。

PR を出す前に、次を確認してください。

- このリポジトリで Coding Agent を使う場合は、[AGENTS.ja.md](AGENTS.ja.md) を読ませてください。
- 明確な理由がない限り、runtime と SDK の前提は .NET 10 のまま保つ。
- English Markdown を変更した場合は、対応する `.ja.md` も更新する。
- 挙動が変わる場合は、test を追加または更新する。

GitHub Releases を changelog の正本とします。release tag は `vX.Y.Z` 形式で作成し、各 tag で framework-dependent の CLI zip asset を公開しつつ、merge 済み pull request から release notes を自動生成します。

release label は必須ではありませんが、付けておくと生成ノートが読みやすくなります。

- `feature`, `enhancement`: user-facing な機能追加
- `fix`, `bug`, `bugfix`: 挙動修正
- `documentation`, `docs`, `meta`, `chore`: docs と保守作業
- `test`, `tests`, `internal`, `refactor`, `ci`, `build`: 内部 test / infrastructure / refactor
- `skip-release-notes`: changelog から外したい PR

label がない PR は、生成される release notes の catch-all である `Other changes` section に入ります。

`dotnet` がない Codex Cloud / 一時環境では、`./scripts/setup-codex-cloud.sh` を実行すると SDK 10 の bootstrap と標準チェックをまとめて実行できます。

可能なら次を実行してください。

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

PR の説明には、何を変えたか、なぜ変えたか、残っている limitation や follow-up work があれば書いてください。
