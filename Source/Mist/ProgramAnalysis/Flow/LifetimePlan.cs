using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Flow;

internal enum LifetimeRelease
{
    None,
    AtExit,
    Guarded,
}

internal readonly record struct LifetimeMark(object Owner, bool Owned, InvocationExpression? Gate = null);

/// <summary>
/// A mark sits on the expression, not its statement, because a throw on either side of a hand-off made
/// mid-expression must see who owns the value.
/// </summary>
internal sealed class LifetimePlan(IReadOnlyDictionary<LocalVariable, LocalDeclarationStatement> declarations)
{
    public static readonly LifetimePlan Empty = new(new Dictionary<LocalVariable, LocalDeclarationStatement>());

    private readonly HashSet<object> covered = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> automatic = new(declarations.Keys.Where(local => LocalLifetimes.IsTracked(local.Type)), ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> owed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> changing = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expression, List<LifetimeMark>> marks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, SyntaxNode> acquisitions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<WithBlockStatement, (object Owner, Holding Holding)> scoped = new(ReferenceEqualityComparer.Instance);

    public bool Covers(LocalDeclarationStatement declaration)
    {
        return declarations.TryGetValue(declaration.Local, out var covering) && ReferenceEquals(covering, declaration);
    }

    public bool Covers(object owner)
    {
        return covered.Contains(owner) || (owner is LocalVariable local && declarations.ContainsKey(local));
    }

    public LifetimeRelease ReleaseOf(LocalDeclarationStatement declaration)
    {
        return Covers(declaration) ? ReleaseOf(declaration.Local) : LifetimeRelease.None;
    }

    public LifetimeRelease ReleaseOf(object owner)
    {
        if(!Covers(owner) || !automatic.Contains(owner) || !owed.Contains(owner))
        {
            return LifetimeRelease.None;
        }

        return changing.Contains(owner) ? LifetimeRelease.Guarded : LifetimeRelease.AtExit;
    }

    public bool NeedsFlag(object owner)
    {
        return ReleaseOf(owner) is LifetimeRelease.Guarded
            || scoped.Values.Any(scope => ReferenceEquals(scope.Owner, owner)
                && (scope.Holding == (Holding.Owned | Holding.Unowned) || (changing.Contains(owner) && owed.Contains(owner))));
    }

    /// <summary>
    /// What a with scope does with the holder whose value it was given: release it on the way out,
    /// unconditionally or under that holder's flag, or not at all when every path has handed it on.
    /// </summary>
    public (object Owner, LifetimeRelease Release)? ReleaseAt(WithBlockStatement scope)
    {
        if(!scoped.TryGetValue(scope, out var found))
        {
            return null;
        }

        var release = found.Holding switch
        {
            Holding.Owned => LifetimeRelease.AtExit,
            Holding.Owned | Holding.Unowned => LifetimeRelease.Guarded,
            _ => LifetimeRelease.None,
        };

        return (found.Owner, release);
    }

    public IReadOnlyList<LifetimeMark> MarksAfter(Expression node)
    {
        return marks.TryGetValue(node, out var found)
            ? found.Where(mark => NeedsFlag(mark.Owner)).ToList()
            : [];
    }

    public bool Gates(InvocationExpression call)
    {
        return marks.Values.Any(found => found.Exists(mark => ReferenceEquals(mark.Gate, call) && NeedsFlag(mark.Owner)));
    }

    public SyntaxNode? AcquiredAt(LocalDeclarationStatement declaration)
    {
        return declaration.Initializer?.Node ?? acquisitions.GetValueOrDefault(declaration.Local);
    }

    internal void Cover(object owner, bool automaticallyDrops = true)
    {
        _ = covered.Add(owner);

        if(automaticallyDrops)
        {
            _ = automatic.Add(owner);
        }
    }

    internal void Owe(object owner)
    {
        _ = owed.Add(owner);
    }

    internal void Mark(Expression at, object owner, bool owned, InvocationExpression? gate = null)
    {
        _ = changing.Add(owner);

        if(!marks.TryGetValue(at, out var found))
        {
            found = [];
            marks.Add(at, found);
        }

        if(!found.Contains(new LifetimeMark(owner, owned, gate)))
        {
            found.Add(new LifetimeMark(owner, owned, gate));
        }

        if(owned)
        {
            _ = acquisitions.TryAdd(owner, at.Node);
        }
    }

    internal void Scope(WithBlockStatement scope, object owner, Holding holding)
    {
        scoped[scope] = (owner, scoped.TryGetValue(scope, out var found) ? found.Holding | holding : holding);

        if(holding.HasFlag(Holding.Owned))
        {
            _ = changing.Add(owner);
        }
    }

    internal void Settle()
    {
        foreach(var (_, (owner, holding)) in scoped)
        {
            if(holding == (Holding.Owned | Holding.Unowned))
            {
                Owe(owner);
            }
        }
    }
}