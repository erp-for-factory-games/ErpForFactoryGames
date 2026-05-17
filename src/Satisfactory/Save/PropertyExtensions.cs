using SatisfactorySaveNet.Abstracts.Model;
using SatisfactorySaveNet.Abstracts.Model.Properties;
using System.Collections.Generic;

namespace Satisfactory.Save;

/// <summary>
/// Convenience accessors over <see cref="ComponentObject.Properties"/>.
///
/// In v1.2+ the fork normalises every property into a single
/// <see cref="RawProperty"/> shape with typed slots (FloatValue,
/// ObjectValue, StringValue, …). These helpers walk by name and pull the
/// expected slot, returning <c>null</c> when absent. Mirrors what
/// fork-side issue #6 proposes adding directly on <c>ComponentObject</c>.
/// </summary>
public static class PropertyExtensions
{
    /// <summary>Reads a float property by name (e.g. <c>mCurrentPotential</c>).</summary>
    public static float? TryGetFloat(this ComponentObject actor, string name)
        => FindRaw(actor, name)?.FloatValue;

    /// <summary>Reads an object reference's <c>PathName</c> by name.</summary>
    public static string? TryGetObjectPath(this ComponentObject actor, string name)
        => FindRaw(actor, name)?.ObjectValue?.PathName;

    /// <summary>Reads a string / name property by name.</summary>
    public static string? TryGetString(this ComponentObject actor, string name)
        => FindRaw(actor, name)?.StringValue;

    /// <summary>Reads an int property by name.</summary>
    public static int? TryGetInt(this ComponentObject actor, string name)
        => FindRaw(actor, name)?.IntValue;

    /// <summary>
    /// Reads an ArrayProperty&lt;StructProperty&gt; by name (e.g. <c>mSplineData</c>
    /// on pipe/belt actors). Returns each element's inner property list, or null
    /// if absent / not an array-of-struct shape.
    /// </summary>
    public static IReadOnlyList<StructElementValue>? TryGetArrayStructValues(this ComponentObject actor, string name)
        => FindRaw(actor, name)?.ArrayStructValues;

    /// <summary>
    /// Returns the trailing class segment of a class path
    /// (e.g. <c>/Game/.../Desc_OreIron.Desc_OreIron_C</c> → <c>Desc_OreIron_C</c>).
    /// Returns the input unchanged when it doesn't contain a <c>'.'</c>.
    /// </summary>
    public static string? ShortClassName(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    private static RawProperty? FindRaw(ComponentObject actor, string name)
    {
        foreach (var p in actor.Properties)
        {
            if (p is RawProperty raw && p.Name == name) return raw;
        }
        return null;
    }
}
