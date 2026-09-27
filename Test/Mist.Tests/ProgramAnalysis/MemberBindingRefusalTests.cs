using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: bind hand-built expression and statement syntax, including failed values passed onward.
[Trait("Tier", "2")]
public sealed class MemberBindingRefusalTests
{
    [Theory]
    [InlineData("unresolved-identifier", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData("unresolved-receiver-called-as-argument", "MIST_PM_UNRESOLVED_IDENTIFIER")]
    [InlineData("refused-member-access-as-an-argument", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("refused-conditional-member-access-as-an-argument", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("refused-static-member-as-an-argument", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("refused-member-call-as-an-argument", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("local-from-a-refused-member-access-passed-on", "MIST_PM_UNRESOLVED_MEMBER")]
    [InlineData("local-from-an-ambiguous-call-passed-on", "MIST_PM_AMBIGUOUS_OVERLOAD")]
    [InlineData("local-from-an-ambiguous-index-passed-on", "MIST_PM_AMBIGUOUS_OVERLOAD")]
    [InlineData("unknown-receiver-member-reaches-its-call", "MIST_PM_UNRESOLVABLE_OVERLOAD")]
    public void RefusalSurvivesBindingWithoutDownstreamCascades(string refusal, string diagnosticId)
    {
        var f = new BindingFragments();
        var intType = f.Built.Type("Int32");
        var sink = HandBuiltModel.Class("Console");
        _ = BindingFragments.Member(sink, "WriteLine", f.Built.Type("Void"), intType);
        _ = BindingFragments.Member(sink, "WriteLine", f.Built.Type("Void"), f.Built.Type("String"));
        _ = f.Local("console", sink);
        ExpressionNode value;
        if(refusal.StartsWith("unresolved", StringComparison.Ordinal))
        {
            value = BindingFragments.Expression(BindingFragments.Identifier("missing"),
                refusal == "unresolved-identifier" ? [] : [Call("Describe")]);
        }
        else if(refusal == "local-from-an-ambiguous-call-passed-on")
        {
            var owner = HandBuiltModel.Class("Probe");
            _ = BindingFragments.Member(owner, "Pick", intType, f.Built.Type("UInt32"));
            _ = BindingFragments.Member(owner, "Pick", intType, f.Built.Type("Int16"));
            _ = f.Local("api", owner);
            _ = f.Local("b", f.Built.Type("Byte"));
            value = BindingFragments.Expression(BindingFragments.Identifier("api"), Call("Pick", BindingFragments.Expression(BindingFragments.Identifier("b"))));
        }
        else if(refusal == "local-from-an-ambiguous-index-passed-on")
        {
            var owner = HandBuiltModel.Class("Shelf");
            owner.IndexersList.Add(new Indexer(intType, [new Parameter("slot", f.Built.Type("UInt32"))], DeclarationFragments.Public) { DeclaringType = owner });
            owner.IndexersList.Add(new Indexer(intType, [new Parameter("slot", f.Built.Type("Int16"))], DeclarationFragments.Public) { DeclaringType = owner });
            _ = f.Local("shelf", owner);
            _ = f.Local("b", f.Built.Type("Byte"));
            var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
            index.Index = BindingFragments.Expression(BindingFragments.Identifier("b"));
            value = BindingFragments.Expression(BindingFragments.Identifier("shelf"), index);
        }
        else if(refusal == "refused-static-member-as-an-argument")
        {
            f.Register(HandBuiltModel.Class("Math"));
            value = BindingFragments.Expression(BindingFragments.Identifier("Math"), Call("Rond"));
        }
        else
        {
            var type = refusal == "refused-conditional-member-access-as-an-argument"
                ? f.Built.Interner.GetOrCreateUnionType([f.Built.Type("String"), f.Built.Type("Void")])
                : refusal == "unknown-receiver-member-reaches-its-call" ? UnknownType.Instance : intType;
            _ = f.Local("k", type);
            PostfixSuffixNode suffix;
            if(refusal == "refused-conditional-member-access-as-an-argument")
            {
                var optional = BindingFragments.At<OptionalMemberAccessSuffixPostfixSuffixNode>();
                optional.Name = BindingFragments.Token("Lenght");
                suffix = optional;
            }
            else
            {
                suffix = refusal == "refused-member-call-as-an-argument" ? Call("Nope") : Access(refusal == "unknown-receiver-member-reaches-its-call" ? "Length" : "Lenght");
            }

            value = BindingFragments.Expression(BindingFragments.Identifier("k"), suffix);
        }

        if(refusal.StartsWith("local-from-", StringComparison.Ordinal))
        {
            var declaration = Declare("bad", value);
            var bound = Assert.IsType<LocalDeclarationStatement>(StatementBinder.BindStatement(declaration, f.Binding, f.Builder));
            Assert.True(bound.Local.IsFailed);
            value = BindingFragments.Expression(BindingFragments.Identifier("bad"));
        }

        _ = f.Bind(BindingFragments.Identifier("console"), Call("WriteLine", value));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal(diagnosticId, diagnostic.Id.Value);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("struct-local-missing-member-does-not-cascade")]
    public void StructLocalMissingMemberDoesNotCascade(string refusal)
    {
        var f = new BindingFragments();
        var point = HandBuiltModel.Struct("Point");
        point.FieldsList.Add(new Field("x", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = point });
        point.FieldsList.Add(new Field("y", f.Built.Type("Int32"), OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = point });
        f.Register(point);
        var uninitialized = new UninitializedVariableStatementStatementNode
        {
            UninitializedVariable = new UninitializedVariableDeclarationNode { Type = BindingFragments.TypeName("Point"), Name = BindingFragments.Token("p") },
        };
        var declared = StatementBinder.BindStatement(uninitialized, f.Binding, f.Builder);
        var target = f.Bind(BindingFragments.Identifier("p"), Access("x"));
        var assignment = new AssignmentExpression(HandBuiltModel.Node(20), target, HandBuiltModel.Literal(f.Built.Type("Int32"), 1L), f.Built.Type("Int32"));
        var bad = StatementBinder.BindStatement(Declare("bad", BindingFragments.Expression(BindingFragments.Identifier("p"), Access("missing"))), f.Binding, f.Builder);
        var root = HandBuiltModel.Block(declared, new ExpressionStatement(assignment), bad);
        Assert.Empty(f.Built.Analyze(new DefiniteAssignmentAnalyzer(), root));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.True(diagnostic.Id.Value == "MIST_PM_UNRESOLVED_MEMBER", refusal);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    private static VariableDeclarationStatementStatementNode Declare(string name, ExpressionNode initializer)
    {
        return new()
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Initializer = initializer },
        };
    }

    private static MemberAccessSuffixPostfixSuffixNode Access(string name)
    {
        var node = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        node.Name = BindingFragments.Token(name);
        return node;
    }

    private static MethodCallSuffixPostfixSuffixNode Call(string name, params ExpressionNode[] arguments)
    {
        var node = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        node.Name = BindingFragments.Token(name);
        node.Arguments = BindingFragments.Arguments(arguments);
        return node;
    }
}