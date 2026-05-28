using Xunit;

// Serialise every UI test class. Each test class still owns its own
// AspireAppFixture (via IClassFixture) which boots a fresh AppHost +
// apiservice + webfrontend + SQLite store, so without serialisation
// xUnit happily fires several boots in parallel — and the apiservice
// loses the startup race (Connection refused) or hits a SQLite
// UNIQUE constraint on the shared Players seed. Either way the
// downstream MyAgentsTests / SmokeTests fail unpredictably.
//
// We can't safely move to a shared ICollectionFixture: MyAgentsTests's
// `Mint_flow_displays_plaintext_token_once` asserts
// `ToHaveCountAsync(1)` against the tokens table, which is only true
// against a fresh DB. Serialising-but-still-per-class keeps that
// invariant intact at the cost of ~50s of extra boot time per CI run.
// See https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/257.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
