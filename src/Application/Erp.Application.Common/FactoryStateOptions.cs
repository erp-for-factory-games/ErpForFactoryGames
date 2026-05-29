namespace Erp.Application.Common;

public sealed class FactoryStateOptions
{
    /// <summary>
    /// Configured save-file path. May point at a specific <c>.sav</c> file or a
    /// SaveGames directory; in the directory case the adapter picks the most
    /// recently written save. Falls back to env var
    /// <c>ERP_SATISFACTORY_SAVE_PATH</c> then auto-detect under
    /// <c>%LocalAppData%\FactoryGame\Saved\SaveGames\</c>.
    /// </summary>
    public string? SavePath { get; set; }
}
