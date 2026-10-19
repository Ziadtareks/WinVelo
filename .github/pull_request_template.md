## What this changes

<!-- One or two sentences. Link the issue if there is one: Fixes #123 -->

## Type

- [ ] Bug fix
- [ ] New or changed tweak (catalog)
- [ ] App / CLI feature
- [ ] Docs, build or CI

## Checks

- [ ] `dotnet build WinVelo.sln -c Release` has 0 warnings
- [ ] `dotnet test src/WinVelo.Tests/WinVelo.Tests.csproj` passes
- [ ] `dotnet run --project tools/CatalogValidator/CatalogValidator.csproj` reports 0 errors
- [ ] For catalog changes: `dotnet run --project tools/PresetGenerator/PresetGenerator.csproj` and `dotnet run --project tools/CatalogStats/CatalogStats.csproj -- --update` were run and their changes are committed
- [ ] For catalog changes: the tweak has sources, side effects and a working undo, and was tested with `--test-mode`
- [ ] New UI text exists in English and Arabic (`src/WinVelo.App/Localization`)
- [ ] CHANGELOG.md has an entry under "Unreleased"
