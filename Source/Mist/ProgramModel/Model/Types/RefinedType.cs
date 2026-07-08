using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A refinement type — a base type narrowed by a predicate constraint.
/// Produced from type alias declarations with refinement predicates
/// (<c>type Percentage = int >= 0 and &lt;= 100;</c>) or adhoc parameter
/// constraints (<c>where x >= 0</c>).
///
/// The predicate is stored as its raw AST node; interpretation and
/// enforcement happen in later phases once the pattern infrastructure
/// is in place.
/// </summary>
internal sealed class RefinedType : MistType, IRefinedType
{
    public RefinedType(IMistType baseType, SyntaxNode predicateNode)
        : base(BuildName(baseType, predicateNode), "", new Scope(null), declaringType: null)
    {
        BaseType = baseType;
        PredicateNode = predicateNode;
    }

    /// <summary>
    /// The underlying type being refined (e.g., <c>Int32</c> for <c>int >= 0</c>).
    /// </summary>
    public IMistType BaseType { get; }

    /// <summary>
    /// The AST node representing the refinement predicate. This is either a
    /// <c>RefinementPredicateNode</c> (from a type alias RHS) or an
    /// <c>AndPatternNode</c> (from an adhoc where clause).
    /// Opaque until the pattern resolution phase interprets it.
    /// </summary>
    public SyntaxNode PredicateNode { get; }

    IMistType IRefinedType.BaseType => BaseType;

    private static string BuildName(IMistType baseType, SyntaxNode predicateNode)
    {
        // For display purposes, use the base type name with a marker.
        // A more descriptive name can be produced later when predicate
        // printing infrastructure is available.
        var span = predicateNode.Span;
        return span.Length > 0
            ? $"{baseType.Name}{{refined}}"
            : $"{baseType.Name}{{refined}}";
    }
}