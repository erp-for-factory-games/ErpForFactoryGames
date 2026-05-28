namespace Erp.Domain.Common;

public readonly record struct BuildingId(string Value)
{
    public override string ToString() => Value;
}
