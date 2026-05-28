using Erp.Domain.Common;

namespace Erp.Application.Common;

/// <summary>
/// Source of live factory state — what's actually placed in the player's save.
/// Mirrors <see cref="ICatalogProvider"/>'s shape: in-memory snapshot, loaded
/// on demand, replaceable via <see cref="LoadFromPath"/>.
/// </summary>
public interface IFactoryStateProvider
{
    bool IsLoaded { get; }
    string? Source { get; }

    LiveFactoryState Current { get; }

    FactoryStateStatus GetStatus();

    /// <summary>
    /// Loads the factory state from the given save file or save directory,
    /// replacing the current snapshot. Returns the resulting status (with
    /// warnings on partial parse) or throws on hard failure.
    /// </summary>
    FactoryStateStatus LoadFromPath(string savePath);

    /// <summary>
    /// Re-parses the currently-loaded save (if any) to pick up changes
    /// from external sources like manual node overrides. No-op when
    /// nothing is loaded. Returns the resulting status.
    /// </summary>
    FactoryStateStatus Refresh();
}
