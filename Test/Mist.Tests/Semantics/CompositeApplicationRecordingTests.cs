using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class CompositeApplicationRecordingTests
{
    private readonly ProgramModelClass programModel = new();
    private readonly ClassType widget = new("Widget", "Probe", new Scope(null), null, null);
    private readonly ClassType gadget = new("Gadget", "Probe", new Scope(null), null, null);
    private readonly ScopeResolver resolver;
    private readonly FileScope scope;

    public CompositeApplicationRecordingTests()
    {
        var diagnostics = new DiagnosticBag();
        _ = programModel.AddSelfAssembly("Test");
        var context = new BuilderContext(programModel, diagnostics, NullAmbientTypeProvider.Instance);
        _ = programModel.GetOrAddNamespace("Probe");
        _ = programModel.RegisterType(widget);
        _ = programModel.RegisterType(gadget);
        scope = new FileScope(context.ScopeFor(programModel.SelfAssembly!), programModel, diagnostics);
        scope.DeclareImport("Probe");
        resolver = new ScopeResolver(context);
    }

    [Fact]
    public void AWrittenUnionIsRecordedOnceWithItsConstituents()
    {
        var union = new TypeReferenceNode
        {
            First = Named("Widget"),
            SingleTypeRefs = [Named("Gadget")],
        };

        _ = resolver.Resolve(union, scope);

        AssertRecorded(CompositeKind.Union, union, widget, gadget);
    }

    [Fact]
    public void AWrittenTupleIsRecordedOnceWithItsElements()
    {
        var tuple = new TupleSingleTypeRefSingleTypeRefNode
        {
            First = new TupleTypeElementNode { Type = BindingFragments.TypeName("Widget") },
            Rest = new TupleTypeElementNode { Type = BindingFragments.TypeName("Gadget") },
        };

        _ = resolver.Resolve(new TypeReferenceNode { First = tuple }, scope);

        AssertRecorded(CompositeKind.Tuple, tuple, widget, gadget);
    }

    [Fact]
    public void AWrittenArrayIsRecordedWithItsElementType()
    {
        var array = Named("Widget");
        array.ArrayRank = new UnsizedArrayRankArrayRankSuffixNode();

        _ = resolver.Resolve(new TypeReferenceNode { First = array }, scope);

        AssertRecorded(CompositeKind.Array, array, widget);
    }

    [Fact]
    public void ANamedTypeRecordsNoComposite()
    {
        _ = resolver.Resolve(BindingFragments.TypeName("Widget"), scope);

        Assert.Empty(programModel.CompositeApplications.Applications);
    }

    private static NamedSingleTypeRefSingleTypeRefNode Named(string name)
    {
        return new NamedSingleTypeRefSingleTypeRefNode { Name = BindingFragments.Name(name) };
    }

    private void AssertRecorded(CompositeKind kind, Storm.Runtime.SyntaxNode node, params MistType[] parts)
    {
        var application = Assert.Single(programModel.CompositeApplications.Applications);
        Assert.Equal(kind, application.Kind);
        Assert.Same(node, application.Node);
        Assert.Equal(parts, application.Parts);
    }
}