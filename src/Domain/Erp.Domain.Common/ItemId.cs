namespace Erp.Domain.Common;

public readonly record struct ItemId(string Value)
{
    public override string ToString() => Value;
}
