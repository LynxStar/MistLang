using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Every union, tuple and array type the source wrote, with the types it was written from. An alias
/// resolves its right-hand side once and every use of the alias reaches the result, so a rule that
/// judges what a composite may hold judges each written one here, once, where it was written.
/// </summary>
internal sealed class CompositeApplicationRegistry
{
    private readonly List<CompositeApplication> applications = [];

    public IReadOnlyList<CompositeApplication> Applications => applications;

    public void Record(CompositeKind kind, IReadOnlyList<IMistType> parts, SyntaxNode node, Scope? boundIn = null)
    {
        applications.Add(new CompositeApplication(kind, parts, node, boundIn));
    }
}

internal enum CompositeKind
{
    Union,
    Tuple,
    Array,
}

internal sealed record CompositeApplication(CompositeKind Kind, IReadOnlyList<IMistType> Parts, SyntaxNode Node, Scope? BoundIn = null);