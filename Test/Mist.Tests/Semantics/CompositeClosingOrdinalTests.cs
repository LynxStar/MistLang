using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class CompositeClosingOrdinalTests
{
    private readonly HandBuiltModel model = new();
    private readonly TypeParameter t = new("T", 0, new Scope(null), null);
    private readonly TypeParameter u = new("U", 1, new Scope(null), null);
    private readonly ClassType first = HandBuiltModel.Class("First");
    private readonly ClassType second = HandBuiltModel.Class("Second");
    private readonly CompositeApplicationRegistry registry = new();

    [Fact]
    public void ALaterParameterIsClosedByItsOwnArgumentAndAForeignOneByNone()
    {
        var foreign = new TypeParameter("V", 0, new Scope(null), null);
        var open = model.Interner.GetOrCreateUnionType([u, foreign]);

        registry.RecordClosing(open, [t, u], [first, second], HandBuiltModel.Node(37), null);

        var application = Assert.Single(registry.Applications);
        Assert.Equal(CompositeKind.Union, application.Kind);
        Assert.Equal([second], application.Parts);
    }

    [Fact]
    public void ANamedTupleIsRecordedAsTheTupleItNames()
    {
        var open = new NamedTupleType(model.Interner.GetOrCreateTupleType([t, model.Type("String")]), ["key", "label"]);

        registry.RecordClosing(open, [t], [first], HandBuiltModel.Node(37), null);

        var application = Assert.Single(registry.Applications);
        Assert.Equal(CompositeKind.Tuple, application.Kind);
        Assert.Equal([first], application.Parts);
    }
}