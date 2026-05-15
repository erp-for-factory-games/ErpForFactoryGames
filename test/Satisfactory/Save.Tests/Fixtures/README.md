# Save fixtures

Bundled `.sav` files for parser tests so dev work doesn't need Satisfactory
installed. Total weight stays under 1 MB.

## Layout

- `v1_0/` — Update 1.0 era (Sept 2024). The fork should accept these
  without IsConveyor / ConveyorChainActor ExtraData (#64 work targets v1.2).
- `v1_2/` — Update 1.2 (May 2026). The three named saves come from the
  upstream `SatisfactorySaveNet` test corpus and match its naming convention:
  - `EmptyWorld.sav` — fresh world, no Hub.
  - `TheHub.sav` — just past the tutorial, Hub placed.
  - `Pedestal.sav` — Hub + a Pedestal building, minimum surface for
    typed-property tests.

## Adding new fixtures

1. Drop the `.sav` under the right version folder.
2. Keep names short (no spaces, no version-in-filename — the folder is
   already versioned).
3. Update `SaveFixtureTests.cs` to add the new fixture to the theory data
   if there's anything version-specific to assert.
4. Keep individual files under 500 KB. Large factory saves bloat the repo
   and don't exercise the parser more than a small one.

## Live save still works

`SaveFileReaderParityTests` auto-detects the latest save on disk via
`SaveFileResolver.AutoDetectLatestSave()` and runs against that when present.
The fixture tests run unconditionally; both stay green on a machine without
the game.
