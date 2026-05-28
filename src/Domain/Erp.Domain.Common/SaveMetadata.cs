namespace Erp.Domain.Common;

public sealed record SaveMetadata(
    string SessionName,
    int SaveVersion,
    int BuildVersion,
    TimeSpan PlayedTime,
    DateTime SaveDateTimeUtc);
