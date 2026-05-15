using Microsoft.JSInterop;

namespace Web;

/// <summary>
/// Tracks whether the user has completed or dismissed the first-load setup
/// wizard. Backed by <c>localStorage</c> so it survives a page reload but is
/// per-browser (no server-side persistence yet — see issue #83 "out of scope").
/// </summary>
public sealed class SetupState(IJSRuntime js)
{
    private const string DismissedKey = "erp-setup-dismissed";

    public async ValueTask<bool> IsDismissedAsync()
    {
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", DismissedKey);
            return string.Equals(raw, "true", StringComparison.Ordinal);
        }
        catch
        {
            // Server-side pre-render or no JS — assume not dismissed so a fresh
            // user still gets the wizard on the first interactive render.
            return false;
        }
    }

    public async ValueTask SetDismissedAsync(bool value)
    {
        try
        {
            if (value)
            {
                await js.InvokeVoidAsync("localStorage.setItem", DismissedKey, "true");
            }
            else
            {
                await js.InvokeVoidAsync("localStorage.removeItem", DismissedKey);
            }
        }
        catch
        {
            // No JS — silently no-op. The dismissed flag is best-effort UX, not a
            // correctness constraint.
        }
    }
}
