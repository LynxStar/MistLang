using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The Standard types indexed by a dependent LENGTH, and how each spells in C# without it. The length
/// is a compile-time index with no runtime witness, so it cannot appear in the emitted type: an array
/// is <c>T[]</c> and a span is the CLR span over one argument, both of which the framework already
/// has and neither of which Mist may duplicate.
/// <para>
/// CONSULTED AHEAD OF THE UNKNOWN FALLBACK, which is the whole reason this is one place rather than a
/// case per type. Every one of these reaches emission with <c>unknown</c> in the length position
/// whenever the length is not statically known, and the fallback spells such a construction
/// <c>dynamic</c> -- a type no framework API accepts and no span operation survives.
/// </para>
/// </summary>
internal static class LengthErasure
{
    private static readonly HashSet<string> LengthIndexed = new(StringComparer.Ordinal)
    {
        WellKnownTypes.Array.FullyQualifiedName,
        WellKnownTypes.Span.FullyQualifiedName,
        WellKnownTypes.ReadOnlySpan.FullyQualifiedName,
    };

    /// <summary>
    /// The C# spelling of a length-indexed construction, or null when the definition is not one. The
    /// element type is emitted through the caller's own <paramref name="emit"/> so a nested
    /// construction takes the same route this one did.
    /// </summary>
    public static string? Emit(ConstructedType constructed, Func<IMistType, string> emit)
    {
        if(!LengthIndexed.Contains(constructed.Definition.FullyQualifiedName)
            || constructed.TypeArguments.Count == 0)
        {
            return null;
        }

        var element = emit(constructed.TypeArguments[0]);

        // An array has no generic spelling at all: C# writes the element type and brackets, where a
        // span is an ordinary one-argument generic.
        return ClrSpelling.Spelling(constructed.Definition) is { } clrName
            ? $"{clrName}<{element}>"
            : $"{element}[]"
            ;
    }

    public static bool IsSpan(IMistType type)
    {
        return type is ConstructedType constructed
            && LengthIndexed.Contains(constructed.Definition.FullyQualifiedName)
            && constructed.Definition.FullyQualifiedName != WellKnownTypes.Array.FullyQualifiedName
            ;
    }
}