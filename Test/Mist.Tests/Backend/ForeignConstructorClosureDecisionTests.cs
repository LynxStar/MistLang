using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.GenericDefaultShapes;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ForeignConstructorClosureDecisionTests
{
    [Fact]
    public void ForeignClosure_IsDiagnosedAndSkipped_AndTheFollowingSourceClosureKeepsItsWitness()
    {
        var model = new HandBuiltModel().Model;
        var source = model.AddSelfAssembly("Probe");
        var shapes = new GenericDefaultShapes();
        var factory = shapes.Type("Factory<,>");
        factory.Assembly = source;
        var product = Parameter(factory);
        var argument = Parameter(factory, "A");
        product.ConstraintsList.Add(new ConstructorConstraint([argument]));

        var foreign = new ClassType("StringBuilder", "System.Text", new(null), null, null) { Assembly = MistAssembly.Ambient };
        _ = Constructor(foreign, shapes.Integer);
        var widget = shapes.Type("Widget");
        widget.Assembly = source;
        _ = Constructor(widget, shapes.String);

        model.ConstructorConstraintClosures.RecordType(factory, [foreign, shapes.Integer], HandBuiltModel.Node(7, "closures.mist"));
        model.ConstructorConstraintClosures.RecordType(factory, [widget, shapes.String], HandBuiltModel.Node(21, "closures.mist"));
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        var closures = ConstraintClosureMap.Build(model, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0016", refusal.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, refusal.Severity);
        var location = Assert.IsType<SourceRange>(refusal.Location);
        Assert.Equal(("closures.mist", 7, 1), (location.Path, location.Offset, location.Length));
        Assert.Collection(refusal.Arguments,
            type => Assert.Equal("StringBuilder", Assert.IsType<TypeValue>(type).Spelling),
            parameters =>
            {
                var signature = Assert.IsType<SequenceValue>(parameters);
                Assert.Equal("int", Assert.IsType<TypeValue>(Assert.Single(signature.Items)).Spelling);
            });
        Assert.Empty(closures.For(foreign));
        var witness = Assert.Single(closures.For(widget));
        Assert.Same(shapes.String, Assert.Single(witness.Factory));
    }
}