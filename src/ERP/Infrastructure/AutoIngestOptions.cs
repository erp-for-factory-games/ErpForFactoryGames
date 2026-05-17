namespace ERP.Infrastructure;

/// <summary>
/// Bound from <c>FactoryState:Satisfactory:AutoIngest</c>. Drives the
/// TickerQ-backed background job that watches the SaveGames directory and
/// dispatches <see cref="ERP.Application.Commands.IngestSave.IngestSaveCommand"/>
/// when a newer <c>.sav</c> appears (#115). Off by default — opt-in until
/// proven harmless in real play.
/// </summary>
public sealed class AutoIngestOptions
{
    /// <summary>
    /// When <c>true</c>, the host startup ensures a cron entry exists that
    /// fires once per minute. When <c>false</c>, the entry is removed if
    /// present — no background work runs.
    /// </summary>
    public bool Enabled { get; set; }
}
