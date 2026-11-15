# Shared training engine package (plan)

Goal: TRAZO and DietoExpress use the SAME generation algorithm, and an improvement made in one reaches the other without
copying files by hand.

## Map

- Pure engine: TRAZO `src/Trazo.Training/Engine/*` (namespace `Trazo.Training`) = DietoExpress `Anguloso.Server/Logica/Training*.cs`
  (namespace `Anguloso.Server.Logica`, CRLF).
- Adapters, NOT part of the engine: TRAZO `src/Trazo.Application/Planning/*` (`LimitationRules`, `MesocycleGenerator`,
  `MacrocyclePlanner`); DietoExpress `Controllers/TrainingPeriodization/TrainingPeriodizationController.Generation.cs`.

## Steps

1. Own repository with a neutral namespace (for example `Training.Engine`), `.gitattributes` with CRLF, its own tests
   (including the engine coaching-quality tests and the 22 TRAZO profiles, which today depend on TRAZO's Planning layer and
   `PlanTestCatalog`).
2. Publish as a private NuGet package on GitHub Packages, package version = engine version. Alternative: git submodule.
3. Each project keeps only its adapter. `LimitationRules` moves into the package once DietoExpress decides where it stores
   the limitation areas.
4. Until the package exists: a sync script plus a CI check that compares the engine files of both repos ignoring namespace and
   line endings and fails when they differ.

## Logic that lives in the TRAZO adapter and must move into the package

- **Automatic deload placement** (`DeloadPlacement`, `MacrocyclePlanner`). The engine's `TrainingMacrocycleDraftBuilder` only
  knows a fixed interval (4-8 training weeks), which leaves a short last block (12 weeks with an interval of 4 end with a
  2-week block). The adapter computes the deload weeks (no run longer than four training weeks, even runs, never the last
  week, none for plans of 6 weeks or less) and rebuilds the phases over the engine's draft. In the package, the builder should
  take either an explicit list of deload weeks or an "automatic" mode implementing `DeloadPlacement.Weeks`, so the adapter
  disappears. The tests in `DeloadPlacementTests` describe the expected behaviour.
- DietoExpress needs the same behaviour when it exposes deload weeks to its professionals.
