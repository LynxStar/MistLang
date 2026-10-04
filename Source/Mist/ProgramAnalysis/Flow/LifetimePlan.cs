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

internal readonly record struct LifetimeMark(LocalVariable Local, bool Owned);

/// <summary>
/// A mark sits on the expression, not its statement, because a throw on either side of a hand-off made
/// mid-expression must see who owns the value.
/// </summary>
internal sealed class LifetimePlan(IReadOnlyDictionary<LocalVariable, LocalDeclarationStatement> declarations)
{
    public static readonly LifetimePlan Empty = new(new Dictionary<LocalVariable, LocalDeclarationStatement>());

    private readonly HashSet<LocalVariable> owed = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> changing = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expression, List<LifetimeMark>> marks = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, SyntaxNode> acquisitions = new(ReferenceEqualityComparer.Instance);

    public bool Covers(LocalDeclarationStatement declaration)
    {
        return declarations.TryGetValue(declaration.Local, out var covered) && ReferenceEquals(covered, declaration);
    }

    public LifetimeRelease ReleaseOf(LocalDeclarationStatement declaration)
    {
        if(!Covers(declaration) || !owed.Contains(declaration.Local))
        {
            return LifetimeRelease.None;
        }

        return changing.Contains(declaration.Local) ? LifetimeRelease.Guarded : LifetimeRelease.AtExit;
    }

    public IReadOnlyList<LifetimeMark> MarksAfter(Expression node)
    {
        return marks.TryGetValue(node, out var found)
            ? found.Where(mark => ReleaseOf(declarations[mark.Local]) == LifetimeRelease.Guarded).ToList()
            : [];
    }

    public SyntaxNode? AcquiredAt(LocalDeclarationStatement declaration)
    {
        return declaration.Initializer?.Node ?? acquisitions.GetValueOrDefault(declaration.Local);
    }

    internal void Owe(LocalVariable local)
    {
        _ = owed.Add(local);
    }

    internal void Mark(Expression at, LocalVariable local, bool owned)
    {
        _ = changing.Add(local);

        if(!marks.TryGetValue(at, out var found))
        {
            found = [];
            marks.Add(at, found);
        }

        if(!found.Contains(new LifetimeMark(local, owned)))
        {
            found.Add(new LifetimeMark(local, owned));
        }

        if(owned)
        {
            _ = acquisitions.TryAdd(local, at.Node);
        }
    }
}