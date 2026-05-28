using Erp.Domain.Common;

namespace Erp.Application.Common;

/// <summary>
/// Resolves the <see cref="PlayerId"/> for the current request scope
/// (ADR-0025 §2). v2 has no real auth so the adapter returns the
/// configured <c>Auth:DevPlayerId</c>; when login lands the adapter
/// reads from the authenticated principal instead.
/// </summary>
public interface ICurrentPlayer
{
    PlayerId Id { get; }
}
