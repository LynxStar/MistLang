using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed class FrontendDiagnosticProducerTests
{
    [Fact]
    [Trait("Tier", "1")]
    public void OverloadAritiesRemainOrderedIntegerFactsAndKeepTheirWording()
    {
        var built = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        var first = Candidate(built, built.Type("Int32"), built.Type("Int32"));
        var second = Candidate(built, built.Type("String"));
        var choice = new OverloadChoice<Method> { Selected = null, NoArityMatch = true };

        OverloadFailure.Report(choice, [first, second, first], [], "method 'Run'", HandBuiltModel.Node(), diagnostics);

        var finding = Assert.Single(diagnostics.Admitted);
        _ = Assert.IsType<SymbolValue>(finding.Arguments[0]);
        var arities = Assert.IsType<SequenceValue>(finding.Arguments[2]);
        Assert.Equal(new Int128[] { 1, 2 }, arities.Items.Select(item => Assert.IsType<QuantityValue>(item).Value));
        Assert.Equal("There is no overload of method 'Run' accepting 0 argument(s). Declared arities: 1, 2.", finding.Message);
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AmbiguousOverloadsSnapshotOrderedSignaturesBeforeModelMutation()
    {
        var built = new HandBuiltModel();
        var diagnostics = new DiagnosticBag();
        var first = Candidate(built, built.Type("Int32"));
        var second = Candidate(built, built.Type("String"));
        var choice = new OverloadChoice<Method> { Selected = null, Ambiguous = [first, second] };

        OverloadFailure.Report(choice, [first, second], [], "method 'Run'", HandBuiltModel.Node(), diagnostics);
        first.ParametersList[0].Type = built.Type("Boolean");

        var finding = Assert.Single(diagnostics.Admitted);
        var candidates = Assert.IsType<SequenceValue>(finding.Arguments[1]);
        Assert.Equal(new[] { "(int)", "(string)" }, candidates.Items.Select(item => Assert.IsType<SymbolValue>(item).Spelling));
        Assert.NotEqual(Assert.IsType<SymbolValue>(candidates.Items[0]).Identity, Assert.IsType<SymbolValue>(candidates.Items[1]).Identity);
        Assert.Equal("The call to method 'Run' is ambiguous. These overloads are equally applicable: (int), (string). Cast an argument to choose one.", finding.Message);
    }

    [Theory]
    [InlineData(false, "MIST0340")]
    [InlineData(true, "MIST0339")]
    [Trait("Tier", "2")]
    public void ConstraintFindingsUseTheirDeclaringClause(bool conflicting, string number)
    {
        var f = new BindingFragments();
        var parameter = new TypeParameter("T", 0, f.Scope, null);
        var declaration = BindingFragments.At<GenericConstraintClauseWhereClauseNode>(12);
        declaration.Name = BindingFragments.Token("T");
        declaration.Constraints = new ConstraintListNode
        {
            Constraint = new ClassConstraintConstraintNode(),
            Constraints = conflicting ? [new StructConstraintConstraintNode()] : [new ClassConstraintConstraintNode()],
        };
        var resolver = new ConstraintResolver(f.Builder, f.Builder.Resolver);

        resolver.Resolve([declaration], [parameter], f.Scope);

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal(number, finding.Number.Value);
        Assert.Equal(DiagnosticHelper.ResolveLocation(declaration).Location, finding.Location);
        Assert.Equal(12, Assert.IsType<SourceRange>(finding.Location).Offset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void AWithheldMemberUseCarriesEveryMatchingReasonOnOneFinding(bool unqualified)
    {
        var f = new BindingFragments();
        var parent = HandBuiltModel.Class("Base");
        var owner = HandBuiltModel.Class("Owner", parent);
        var first = new WithholdingFrame(new SymbolValue("Base.Hidden(int)", "Hidden"), new EnumValue("pointer"));
        var second = new WithholdingFrame(new SymbolValue("Base.Hidden(string)", "Hidden"), new EnumValue("byRefReturn"));
        parent.WithheldMembers.AddRange([first, second]);
        parent.WithheldMembers.Add(new WithholdingFrame(new SymbolValue("Base.Other()", "Other"), new EnumValue("pointer")));
        var node = BindingFragments.Identifier("Hidden");

        if(unqualified)
        {
            var context = new BindingContext(new BodyScope(owner.Scope), null);
            _ = IdentifierBinder.BindIdentifier(node, context, f.Builder);
        }
        else
        {
            _ = MemberResolution.Resolve(owner, "Hidden", MemberVisibility.InstanceAndStatic, node, f.Builder);
        }

        var finding = Assert.Single(f.Diagnostics.Admitted);
        Assert.Equal(new Frame[] { first, second }, finding.Frames);
        Assert.Equal(DiagnosticHelper.ResolveLocation(node).Location, finding.Location);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Tier", "2")]
    public void AWithheldIndexerReasonStaysOnTheImmediateOrDeferredIndexMiss(bool deferred)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Indexed");
        var frame = new WithholdingFrame(new SymbolValue("Indexed.Item(int)", "Item"), new EnumValue("pointer"));
        owner.WithheldMembers.Add(frame);
        var node = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
        if(deferred)
        {
            _ = f.Local("unknown", UnknownType.Instance);
            var access = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
            access.Name = BindingFragments.Token("Length");
            node.Index = BindingFragments.Expression(BindingFragments.Identifier("unknown"), access);
        }
        else
        {
            _ = f.Local("index", f.Built.Type("Int32"));
            node.Index = BindingFragments.Expression(BindingFragments.Identifier("index"));
        }

        var error = Assert.IsType<ErrorExpression>(
            IndexBinder.Bind(node, HandBuiltModel.Literal(owner), f.Binding, f.Builder));

        var finding = deferred ? error.DeferredDiagnostic! : Assert.Single(f.Diagnostics.Admitted);
        if(deferred)
        {
            Assert.Empty(f.Diagnostics.Admitted);
            Assert.True(finding.Ordinal > 0);
        }

        Assert.Same(ProgramModelDiagnostics.ReceiverNotIndexable, finding.Descriptor);
        Assert.Same(frame, Assert.Single(finding.Frames));
    }

    private static Method Candidate(HandBuiltModel built, params IMistType[] types)
    {
        return new Method("Run", built.Type("Void"), types.Select((type, index) => new Parameter($"p{index}", type)).ToList(), DeclarationFragments.Public);
    }

}