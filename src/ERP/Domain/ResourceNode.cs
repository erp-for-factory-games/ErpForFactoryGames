namespace ERP.Domain;

public sealed record ResourceNode(
    string Reference,
    ResourceNodeKind Kind,
    ItemId? Resource,
    NodePurity Purity,
    Position Position);
