using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ProjectedWithholdingBindingTests
{
    [Fact]
    public void AScopeFreeGroupVisitsItsOwnerOnceAndRetainsIndependentEqualReasons()
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Surface");
        _ = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"), f.Built.Type("Int32"));
        _ = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"), f.Built.Type("String"));
        var first = Withheld("Hidden");
        var second = Withheld("Hidden");
        owner.WithheldMembers.AddRange([first, second]);
        var group = new MethodGroupExpression(HandBuiltModel.Node(), null, "Hidden", owner.MethodsList, null);

        var frames = ProjectedWithholding.Frames(group, scope: null, f.Built.Interner);

        Assert.Equal(2, frames.Length);
        Assert.Same(first, frames[0]);
        Assert.Same(second, frames[1]);
        Assert.Empty(f.Diagnostics.Admitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitGenericLookupMissKeepsInheritedReasonsOnItsOwningOccurrence(bool inherited)
    {
        var f = new BindingFragments();
        var declaring = HandBuiltModel.Class("Surface");
        var frame = Withheld("Hidden");
        declaring.WithheldMembers.Add(frame);
        var owner = inherited ? HandBuiltModel.Class("Owner", declaring) : declaring;
        var context = new BindingContext(new BodyScope(owner.Scope), null);
        var node = BindingFragments.At<GenericInvocationExpressionPrimaryNode>();
        node.Name = BindingFragments.Token("Hidden");
        node.TypeArgs = BindingFragments.TypeArguments("Int32");

        var finding = AssertOwnedFailure(
            () => PostfixBinder.BindGenericInvocation(node, context, f.Builder), f.Diagnostics, deferred: false);

        Assert.Same(ProgramModelDiagnostics.UnresolvedIdentifier, finding.Descriptor);
        Assert.Equal("The name 'Hidden' could not be found in this scope.", finding.Message);
        Assert.Same(frame, Assert.Single(finding.Frames));
        Assert.Equal(DiagnosticHelper.ResolveLocation(node).Location, finding.Location);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void APartiallyProjectedMethodGroupKeepsReasonsOnImmediateAndDeferredFailures(bool qualified, bool deferred)
    {
        var f = new BindingFragments();
        var declaring = HandBuiltModel.Class("Surface");
        var owner = new ClassType("Owner", "Probe", f.Scope, null, null) { BaseType = declaring };
        _ = BindingFragments.Member(declaring, "Hidden", f.Built.Type("Void"), f.Built.Type("Int32"), f.Built.Type("Int32"));
        var frame = Withheld("Hidden");
        declaring.WithheldMembers.Add(frame);
        declaring.WithheldMembers.Add(Withheld("Other"));
        var context = new BindingContext(new BodyScope(owner.Scope), null);
        var node = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        node.Arguments = BindingFragments.Arguments(Argument(f, deferred));
        var group = new MethodGroupExpression(node, qualified ? HandBuiltModel.Literal(owner) : null,
            "Hidden", declaring.MethodsList, null);

        var finding = AssertOwnedFailure(
            () => PostfixBinder.BindInvocation(node, group, node.Arguments, null, context, f.Builder), f.Diagnostics, deferred);

        Assert.Same(ProgramModelDiagnostics.NoApplicableOverload, finding.Descriptor);
        Assert.Equal("There is no overload of method 'Hidden' accepting 1 argument(s). Declared arities: 2.", finding.Message);
        Assert.Same(frame, Assert.Single(finding.Frames));
        Assert.Equal(DiagnosticHelper.ResolveLocation(node).Location, finding.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitGenericPartialGroupKeepsReasonsOnTypeArgumentCountFailures(bool deferred)
    {
        var f = new BindingFragments();
        var owner = new ClassType("Surface", "Probe", f.Scope, null, null);
        _ = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"), f.Built.Type("Int32"));
        var frame = Withheld("Hidden");
        owner.WithheldMembers.Add(frame);
        var context = new BindingContext(new BodyScope(owner.Scope), null);
        var node = BindingFragments.At<GenericInvocationExpressionPrimaryNode>();
        node.Name = BindingFragments.Token("Hidden");
        node.TypeArgs = BindingFragments.TypeArguments("Int32");
        node.Arguments = BindingFragments.Arguments(Argument(f, deferred));

        var finding = AssertOwnedFailure(
            () => PostfixBinder.BindGenericInvocation(node, context, f.Builder), f.Diagnostics, deferred);

        Assert.Same(ProgramModelDiagnostics.TypeArgumentCountMismatch, finding.Descriptor);
        Assert.Equal("The call to method 'Hidden' states the wrong number of type arguments. Declared type parameter counts: 0.", finding.Message);
        Assert.Same(frame, Assert.Single(finding.Frames));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnconvertedMethodGroupKeepsItsWithholdingReason(bool qualified)
    {
        var f = new BindingFragments();
        var owner = new ClassType("Surface", "Probe", f.Scope, null, null);
        _ = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"));
        var frame = Withheld("Hidden");
        owner.WithheldMembers.Add(frame);
        f.Binding.Scope = new BodyScope(owner.Scope);
        _ = f.Local("owner", owner);
        var node = BindingFragments.At<PostfixNode>();
        node.Target = BindingFragments.Identifier(qualified ? "owner" : "Hidden");
        var access = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        access.Name = BindingFragments.Token("Hidden");
        node.Suffix = qualified ? [access] : [];

        var finding = AssertOwnedFailure(
            () => PostfixBinder.BindPostfix(node, f.Binding, f.Builder), f.Diagnostics, deferred: false);

        Assert.Same(ProgramModelDiagnostics.MethodGroupNotConverted, finding.Descriptor);
        Assert.Same(frame, Assert.Single(finding.Frames));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARejectedMethodGroupArgumentKeepsItsWithholdingReason(bool qualified)
    {
        var f = new BindingFragments();
        var owner = new ClassType("Surface", "Probe", f.Scope, null, null);
        _ = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"), f.Built.Type("Int32"));
        var frame = Withheld("Hidden");
        owner.WithheldMembers.Add(frame);
        f.Binding.Scope = new BodyScope(owner.Scope);
        var node = HandBuiltModel.Node();
        var group = new MethodGroupExpression(node, qualified ? HandBuiltModel.Literal(owner) : null,
            "Hidden", owner.MethodsList, null);
        var target = new FunctionType([f.Built.Type("String")], f.Built.Type("Void"));

        var finding = AssertOwnedFailure(
            () => Assert.Single(ArgumentBinder.Settle([group], [new Parameter("callback", target)],
                TypeSubstitution.Empty, f.Binding, f.Builder)), f.Diagnostics, deferred: false);

        Assert.Same(ProgramModelDiagnostics.MethodGroupNotConverted, finding.Descriptor);
        Assert.Same(frame, Assert.Single(finding.Frames));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStatedSpreadAddsAWithholdingFrameOnlyWhenRejected(bool acceptsPack)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Surface");
        var method = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"));
        var parameter = BindingFragments.Parameter(method, "T");
        parameter.PackKind = acceptsPack ? TypePackKind.Unbracketed : TypePackKind.None;
        f.Register(new TypeParameter("Ts", 0, f.Scope, null) { PackKind = TypePackKind.Unbracketed });
        var frame = Withheld("Hidden");
        owner.WithheldMembers.Add(frame);
        var spread = BindingFragments.At<SpreadSingleTypeRefSingleTypeRefNode>(40);
        spread.Name = BindingFragments.Name("Ts");
        var stated = new TypeArgumentListNode
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = new TypeReferenceNode { First = spread } },
        };
        var node = HandBuiltModel.Node();
        var group = new MethodGroupExpression(node, HandBuiltModel.Literal(owner), "Hidden", [method], null);

        Expression bind()
        {
            return StatedMethodArguments.Bind(group, stated, "Hidden", node, f.Binding, f.Builder);
        }

        if(acceptsPack)
        {
            _ = Assert.IsType<MethodGroupExpression>(bind());
            Assert.Empty(f.Diagnostics.Admitted);
            return;
        }

        var finding = AssertOwnedFailure(bind, f.Diagnostics, deferred: false);

        Assert.Same(ProgramModelDiagnostics.SpreadNonPackArgument, finding.Descriptor);
        Assert.Equal("'Ts...' cannot fill a fixed type-argument slot of 'Hidden'. A symbolic spread requires a pack slot that accepts every permitted width.", finding.Message);
        Assert.Same(frame, Assert.Single(finding.Frames));
        Assert.Equal(DiagnosticHelper.ResolveLocation(spread).Location, finding.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APartiallyProjectedCollectionAddKeepsReasonsOnImmediateAndDeferredFailures(bool deferred)
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Surface");
        _ = BindingFragments.Member(owner, "Add", f.Built.Type("Void"), f.Built.Type("Int32"), f.Built.Type("Int32"));
        var frame = Withheld("Add");
        owner.WithheldMembers.Add(frame);
        var element = BindingFragments.At<CollectionElementAssignmentPropertyAssignmentNode>();
        element.Value = Argument(f, deferred);
        var block = new PropertyInitializerBlockNode
        {
            Assignments = new PropertyAssignmentListNode { PropertyAssignment = element },
        };

        var finding = AssertOwnedFailure(
            () => Assert.Single(ObjectInitializerBinder.Bind(block, owner, f.Binding, f.Builder)).Value, f.Diagnostics, deferred);

        Assert.Same(ProgramModelDiagnostics.NoApplicableOverload, finding.Descriptor);
        Assert.Same(frame, Assert.Single(finding.Frames));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APartiallyProjectedIndexerGroupKeepsReasonsOnImmediateAndDeferredFailures(bool deferred)
    {
        var f = new BindingFragments();
        var declaring = HandBuiltModel.Class("Surface");
        var owner = HandBuiltModel.Class("Owner", declaring);
        declaring.IndexersList.Add(new Indexer(f.Built.Type("Int32"),
            [new Parameter("first", f.Built.Type("Int32")), new Parameter("second", f.Built.Type("Int32"))], DeclarationFragments.Public)
        {
            DeclaringType = declaring,
        });
        var frame = Withheld("Item");
        declaring.WithheldMembers.Add(frame);
        declaring.WithheldMembers.Add(Withheld("Other"));
        var node = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
        node.Index = Argument(f, deferred);

        var finding = AssertOwnedFailure(
            () => IndexBinder.Bind(node, HandBuiltModel.Literal(owner), f.Binding, f.Builder), f.Diagnostics, deferred);

        Assert.Same(ProgramModelDiagnostics.NoApplicableOverload, finding.Descriptor);
        Assert.Equal("There is no overload of indexer 'Owner.this[]' accepting 1 argument(s). Declared arities: 2.", finding.Message);
        Assert.Same(frame, Assert.Single(finding.Frames));
        Assert.Equal(DiagnosticHelper.ResolveLocation(node).Location, finding.Location);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASuccessfulMethodCallDoesNotReportItsWithheldOverloads(bool qualified)
    {
        var f = new BindingFragments();
        var owner = new ClassType("Surface", "Probe", f.Scope, null, null);
        var method = BindingFragments.Member(owner, "Hidden", f.Built.Type("Void"), f.Built.Type("Int32"));
        owner.WithheldMembers.Add(Withheld("Hidden"));
        var context = new BindingContext(new BodyScope(owner.Scope), null);
        var node = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        node.Arguments = BindingFragments.Arguments(Argument(f, deferred: false));
        var group = new MethodGroupExpression(node, qualified ? HandBuiltModel.Literal(owner) : null,
            "Hidden", owner.MethodsList, null);

        var call = Assert.IsType<InvocationExpression>(
            PostfixBinder.BindInvocation(node, group, node.Arguments, null, context, f.Builder));

        Assert.Same(method, call.Method);
        Assert.Empty(f.Diagnostics.Admitted);
    }

    [Fact]
    public void ASuccessfulIndexerAccessDoesNotReportItsWithheldOverloads()
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Surface");
        var indexer = new Indexer(f.Built.Type("Int32"), [new Parameter("index", f.Built.Type("Int32"))], DeclarationFragments.Public)
        {
            DeclaringType = owner,
        };
        owner.IndexersList.Add(indexer);
        owner.WithheldMembers.Add(Withheld("Item"));
        var node = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
        node.Index = Argument(f, deferred: false);

        var access = Assert.IsType<IndexExpression>(IndexBinder.Bind(node, HandBuiltModel.Literal(owner), f.Binding, f.Builder));

        Assert.Same(indexer, access.Indexer);
        Assert.Empty(f.Diagnostics.Admitted);
    }

    [Fact]
    public void AnUnusedWithheldNameAddsNoFindingWhileAnAvailableMemberIsBound()
    {
        var f = new BindingFragments();
        var owner = HandBuiltModel.Class("Surface");
        owner.WithheldMembers.Add(Withheld("Hidden"));
        var method = BindingFragments.Member(owner, "Visible", f.Built.Type("Void"));
        var node = HandBuiltModel.Node();
        var group = new MethodGroupExpression(node, HandBuiltModel.Literal(owner), "Visible", [method], null);

        _ = Assert.IsType<InvocationExpression>(PostfixBinder.BindInvocation(node, group, null, null, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.Admitted);
    }

    private static Diagnostic AssertOwnedFailure(Func<Expression> bind, DiagnosticBag diagnostics, bool deferred)
    {
        var error = Assert.IsType<ErrorExpression>(bind());
        var finding = deferred ? Assert.IsType<Diagnostic>(error.DeferredDiagnostic) : Assert.Single(diagnostics.Admitted);
        var ordinal = finding.Ordinal;
        Assert.True(ordinal > 0);
        if(deferred)
        {
            Assert.Empty(diagnostics.Admitted);
            diagnostics.Replay(finding, error.Node);
        }

        using(diagnostics.ReplayOwnedFailures())
        {
            ErrorExpression? repeated = null;
            var trial = diagnostics.Withhold(() => repeated = Assert.IsType<ErrorExpression>(bind()));
            if(deferred)
            {
                Assert.Empty(trial.Occurrences);
                Assert.Same(finding, repeated!.DeferredDiagnostic);
            }
            else
            {
                Assert.Same(finding, Assert.Single(trial.Occurrences));
            }

            trial.Discard();
        }

        Assert.Same(finding, Assert.Single(diagnostics.Admitted));
        Assert.Equal(ordinal, finding.Ordinal);
        return finding;
    }

    private static ExpressionNode Argument(BindingFragments f, bool deferred)
    {
        _ = f.Local("argument", deferred ? UnknownType.Instance : f.Built.Type("Int32"));
        if(!deferred)
        {
            return BindingFragments.Expression(BindingFragments.Identifier("argument"));
        }

        var access = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        access.Name = BindingFragments.Token("Length");
        return BindingFragments.Expression(BindingFragments.Identifier("argument"), access);
    }

    private static WithholdingFrame Withheld(string name)
    {
        return new WithholdingFrame(new SymbolValue($"Surface.{name}<T>()", name), new EnumValue("UnresolvableConstraint"));
    }
}