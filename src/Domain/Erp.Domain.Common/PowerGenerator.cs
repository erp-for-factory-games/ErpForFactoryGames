namespace Erp.Domain.Common;

public sealed record PowerGenerator(
    string Reference,
    GeneratorKind Kind,
    Position Position);
