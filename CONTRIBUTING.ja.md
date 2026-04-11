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

正本となる検証コマンドは次です。

```bash
bash scripts/verify-ci.sh
```

この script が repository 所有の検証フローです。ほかの文書では内部の restore / format / build / test 手順を複写したり順序を並べ替えたりせず、この script を参照してください。

Codex Cloud / 一時環境で、PATH 上に互換な .NET 10 SDK が無い場合は、先に `./scripts/setup-codex-cloud.sh` を実行してください。この helper はそのような環境を bootstrap したうえで `bash scripts/verify-ci.sh` へ処理を渡すためのものです。

大きな repository-improvement plan を一時的に保持したい場合は、`.tmp/plans/` 配下に置き、untracked のまま維持してください。その領域を canonical documentation として扱わず、active docs から現行運用の案内としてリンクせず、採用した結論だけを tracked documentation と review 成果物へ反映してください。

PR の説明には、何を変えたか、なぜ変えたか、残っている limitation や follow-up work があれば書いてください。
