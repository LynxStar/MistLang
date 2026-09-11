using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A base type narrowed by a predicate. Named when a type alias declares it
/// (<c>type Percentage = int >= 0 and &lt;= 100;</c>) and anonymous when a parameter's
/// <c>where</c> clause does (<c>where x >= 0</c>), which is the same type without a name to
/// spell it by: an anonymous one displays as its base and is never identical to another, since
/// nothing can name it twice. The predicate is bound once, by PredicateBinder, before any body.
/// </summary>
internal sealed class RefinedType : MistType, IRefinedType
{
    public RefinedType(IMistType baseType, SyntaxNode predicateNode, string? name = null)
        : base(name ?? Anonymous(baseType, predicateNode), new Scope(null), declaringType: null)
    {
        BaseType = baseType;
        PredicateNode = predicateNode;
        IsAnonymous = name is null;
    }

    public bool IsAnonymous { get; }

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

    public Pattern? Predicate { get; internal set; }

    IMistType IRefinedType.BaseType => BaseType;

    private static string Anonymous(IMistType baseType, SyntaxNode predicateNode)
    {
        return $"{baseType.Name}{{where@{predicateNode.Span.Start}}}";
    }
}