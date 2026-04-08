# Contributing

Pull request は歓迎します。

このリポジトリは best-effort で保守しているため、review や follow-up には時間がかかることがあります。小さく焦点の絞れた変更の方が、確認しやすく取り込みやすいです。

PR を出す前に、次を確認してください。

- このリポジトリで Coding Agent を使う場合は、[AGENTS.ja.md](AGENTS.ja.md) を読ませてください。
- 明確な理由がない限り、runtime と SDK の前提は .NET 10 のまま保つ。
- English Markdown を変更した場合は、対応する `.ja.md` も更新する。
- 挙動が変わる場合は、test を追加または更新する。

`dotnet` がない Codex Cloud / 一時環境では、`./scripts/setup-codex-cloud.sh` を実行すると SDK 10 の bootstrap と標準チェックをまとめて実行できます。

可能なら次を実行してください。

```bash
dotnet format whitespace . --folder --verify-no-changes
dotnet test Plateau.ResoniteLink.sln --configuration Release -m:1 -p:UseSharedCompilation=false
```

PR の説明には、何を変えたか、なぜ変えたか、残っている limitation や follow-up work があれば書いてください。
