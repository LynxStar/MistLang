using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// What the backend writes for a Standard type the CLR already has. An intrinsic type is never
/// emitted as a declaration, so every mention of one is a name the emitter SUBSTITUTES, and a
/// substituted name is spelled in full, a keyword counting as such; CSharpEmitterV2.EmitUsings says
/// why no directive covers one. The metadata name beside it is what <see cref="IntrinsicSupply"/>
/// looks the type up by, so the audit and the emitter cannot name two different types.
/// <see cref="AmbientProvider.WellKnownTypeMap"/> maps the other direction, CLR to Mist, for the
/// types that arrive from metadata.
/// </summary>
internal static class ClrSpelling
{
    public readonly record struct Entry(string? Spelling, string MetadataName);

    private static readonly Dictionary<string, Entry> ByMistName = new(StringComparer.Ordinal)
    {
        ["Standard.Int32"] = new("int", "System.Int32"),
        ["Standard.Int64"] = new("long", "System.Int64"),
        ["Standard.UInt32"] = new("uint", "System.UInt32"),
        ["Standard.UInt64"] = new("ulong", "System.UInt64"),
        ["Standard.Int16"] = new("short", "System.Int16"),
        ["Standard.UInt16"] = new("ushort", "System.UInt16"),
        ["Standard.Byte"] = new("byte", "System.Byte"),
        ["Standard.SByte"] = new("sbyte", "System.SByte"),
        ["Standard.Single"] = new("float", "System.Single"),
        ["Standard.Double"] = new("double", "System.Double"),
        ["Standard.Decimal"] = new("decimal", "System.Decimal"),
        ["Standard.Boolean"] = new("bool", "System.Boolean"),
        ["Standard.Char"] = new("char", "System.Char"),
        ["Standard.String"] = new("string", "System.String"),
        ["Standard.Math"] = new("System.Math", "System.Math"),
        ["Standard.Console"] = new("System.Console", "System.Console"),
        ["Standard.Convert"] = new("System.Convert", "System.Convert"),
        ["Standard.Type"] = new("System.Type", "System.Type"),
        ["Standard.Index"] = new("System.Index", "System.Index"),
        ["Standard.Range"] = new("System.Range", "System.Range"),
        ["Standard.Regex"] = new("System.Text.RegularExpressions.Regex", "System.Text.RegularExpressions.Regex"),
        ["Standard.Span<,>"] = new("System.Span", "System.Span`1"),
        ["Standard.ReadOnlySpan<,>"] = new("System.ReadOnlySpan", "System.ReadOnlySpan`1"),
        ["Standard.Task<>"] = new("System.Threading.Tasks.Task", "System.Threading.Tasks.Task`1"),
        ["Standard.ValueTask<>"] = new("System.Threading.Tasks.ValueTask", "System.Threading.Tasks.ValueTask`1"),
        ["Standard.IEnumerable<>"] = new("System.Collections.Generic.IEnumerable", "System.Collections.Generic.IEnumerable`1"),
        ["Standard.IEnumerator<>"] = new("System.Collections.Generic.IEnumerator", "System.Collections.Generic.IEnumerator`1"),
        ["Standard.Array<,>"] = new(Spelling: null, "System.Array"),
        ["Standard.Func<,>"] = new(Spelling: null, "System.Func"),
    };

    public static IReadOnlyCollection<string> MistNames => ByMistName.Keys;

    public static string? Spelling(IMistType definition)
    {
        return ByMistName.GetValueOrDefault(definition.FullyQualifiedName).Spelling;
    }

    public static string? Spelling(WellKnownType type)
    {
        return ByMistName.GetValueOrDefault(type.FullyQualifiedName).Spelling;
    }

    public static string? MetadataName(IMistType definition)
    {
        return ByMistName.TryGetValue(definition.FullyQualifiedName, out var entry) ? entry.MetadataName : null;
    }

    public static bool IsListed(MistType definition)
    {
        return ByMistName.ContainsKey(definition.FullyQualifiedName);
    }
}