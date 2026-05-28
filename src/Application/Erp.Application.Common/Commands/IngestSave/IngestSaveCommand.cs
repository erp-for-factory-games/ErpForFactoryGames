namespace Erp.Application.Common.Commands.IngestSave;

public sealed record IngestSaveCommand(string SavePath);

public static class IngestSaveCommandHandler
{
    public static FactoryStateStatus Handle(IngestSaveCommand command, IFactoryStateProvider provider) =>
        provider.LoadFromPath(command.SavePath);
}
