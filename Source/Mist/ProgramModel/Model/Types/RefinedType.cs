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
    private readonly RefinedType root;

    private Pattern? boundPredicate;

    public RefinedType(IMistType baseType, SyntaxNode predicateNode, string? name = null)
        : base(name ?? Anonymous(baseType, predicateNode), new Scope(null), declaringType: null)
    {
        BaseType = baseType;
        PredicateNode = predicateNode;
        IsAnonymous = name is null;
        root = this;
    }

    /// <summary>
    /// The same refinement over a substituted base, which is what a closed receiver makes of one
    /// written over a type parameter. It SHARES the declaration's predicate cell rather than copying
    /// it: the predicate is bound once, in the scope it was written in and against the base declared
    /// there, so a copy taken before <c>PredicateBinder</c> runs would be empty forever, and a copy
    /// taken after would let the declaration's verdict and this one's disagree.
    /// </summary>
    public RefinedType(RefinedType declared, IMistType baseType)
        : this(baseType, declared.PredicateNode, declared.IsAnonymous ? null : declared.Name)
    {
        IsSubstituted = true;
        root = declared.root;
    }

    public bool IsAnonymous { get; }

    public bool IsSubstituted { get; }

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

    public Pattern? Predicate
    {
        get => root.boundPredicate;
        internal set => root.boundPredicate = value;
    }

    IMistType IRefinedType.BaseType => BaseType;

    private static string Anonymous(IMistType baseType, SyntaxNode predicateNode)
    {
        return $"{baseType.Name}{{where@{predicateNode.Span.Start}}}";
    }
}