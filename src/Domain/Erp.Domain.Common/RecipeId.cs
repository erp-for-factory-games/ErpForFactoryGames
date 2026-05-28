namespace Erp.Domain.Common;

public readonly record struct RecipeId(string Value)
{
    public override string ToString() => Value;
}
