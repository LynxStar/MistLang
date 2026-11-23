using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AutomaticCloneRefusalTests
{
    [Theory]
    [InlineData("member", false)]
    [InlineData("constraint", false)]
    [InlineData("member", true)]
    [InlineData("constraint", true)]
    public void ARefusedCloneNamesTheBlockingFieldAndRemedyAtEachReportSite(string site, bool structure)
    {
        var model = new HandBuiltModel();
        var scope = new Scope(null);
        var style = new ClassType("Style", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        style.FieldsList.Add(new Field("Size", new StructType("Int32", "Standard", scope, null, null), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = style });
        ConcreteType card = structure
            ? new StructType("Card", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            : new ClassType("Card", "Probe", scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        card.FieldsList.Add(new Field("Look", style, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = card });
        model.Model.Clones = new CloneDerivation([style, card]);
        IReadOnlyList<Diagnostic> diagnostics;

        if(site == "member")
        {
            var value = new LocalReferenceExpression(HandBuiltModel.Node(20), new LocalVariable("c", card));
            var access = new MemberAccessExpression(HandBuiltModel.Node(40), value, "Clone", ErrorType.Instance, member: new NotFoundResult());
            var call = new InvocationExpression(HandBuiltModel.Node(40), access, [], ErrorType.Instance, null, null);
            diagnostics = model.Analyze(new OperatorAnalyzer(), HandBuiltModel.Block(new ExpressionStatement(call)), [style, card]);
        }
        else
        {
            var clonable = new AbstractType("IClonable<>", "Standard", scope, null, null);
            _ = DeclarationFragments.Parameter(clonable, scope, "T");
            var copy = DeclarationFragments.Method(card, "Copy", model.Type("Void"));
            var parameter = DeclarationFragments.Parameter(copy, scope, "T");
            parameter.ConstraintsList.Add(new TypeConstraint(model.Interner.GetOrCreateConstructedType(clonable, [parameter])));
            model.Model.ConstructorConstraintClosures.RecordMethod(copy, [card], TypeSubstitution.Empty.With(parameter, card), HandBuiltModel.Node(40));
            diagnostics = model.Analyze(new ConstraintAnalyzer(), types: [style, card]);
        }

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("MIST0506", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(41, diagnostic.Position().Start.Column);
        Assert.Equal(
            "Cannot clone this 'Card': 'Card' cannot be cloned: field 'Look' of 'Card' is not owned and holds 'Style', which can change; make that field 'owned' or give the type a 'Clone()' of its own.",
            diagnostic.Message);
    }

    [Fact]
    public void MissingCloneLookupRetainsTheReceiverForAnalysisWithoutANameError()
    {
        var binding = new BindingFragments();
        var card = new ClassType("Card", "Probe", binding.Scope, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>());
        _ = binding.Local("c", card);
        var callNode = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        callNode.Name = BindingFragments.Token("Clone");
        var call = binding.Bind(BindingFragments.Identifier("c"), callNode);

        Assert.Empty(binding.Diagnostics.ToImmutableArray());
        var access = Assert.IsType<MemberAccessExpression>(Assert.IsType<InvocationExpression>(call).Receiver);
        _ = Assert.IsType<NotFoundResult>(access.Member);
        Assert.False(access.IsFailed);

    }
}