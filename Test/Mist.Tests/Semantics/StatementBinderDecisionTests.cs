using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class StatementBinderDecisionTests
{
    private readonly BindingFragments f = new();

    private IMistType Int => f.Built.Type("Int32");

    private IMistType Optional => f.Built.Interner.GetOrCreateUnionType([Int, f.Built.Type("Void")]);

    [Fact]
    public void AContextBlockMakesItsValueAmbientUnderTheValuesOwnTypeForAScopedBody()
    {
        var context = Local("ctx", Int);
        var inner = Declare("inner", Read("ctx"));

        var with = Assert.IsType<WithBlockStatement>(Bind(new WithBlockStatementStatementNode
        {
            WithBlockStatement = new WithBlockStatementNode { Expression = Read("ctx"), Body = new BlockNode { Statements = [inner] } },
        }));

        Assert.Same(context, Assert.IsType<LocalReferenceExpression>(with.Value).Local);
        Assert.Same(Int, with.KeyType);
        Assert.Null(with.Binding);
        var declared = Assert.IsType<LocalDeclarationStatement>(Assert.Single(with.Body.Statements));
        Assert.Equal("inner", declared.Local.Name);
        Assert.False(f.Scope.Locals.ContainsKey("inner"));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnElseIfIsTheNestedIfInTheElsePosition()
    {
        _ = Local("first", f.Built.Type("Boolean"));
        var second = Local("second", f.Built.Type("Boolean"));

        var outer = Assert.IsType<IfStatement>(Bind(new IfStatementStatementNode
        {
            IfStatement = new IfStatementNode
            {
                Condition = Read("first"),
                ThenBranch = new BlockNode(),
                ElseBranch = new ElseIfElseClauseNode
                {
                    ElseIf = new IfStatementNode
                    {
                        Condition = Read("second"),
                        ThenBranch = new BlockNode { Statements = [AsStatement(Read("second"))] },
                        ElseBranch = new ElseBlockElseClauseNode { ElseBlock = new BlockNode() },
                    },
                },
            },
        }));

        var nested = Assert.IsType<IfStatement>(outer.ElseBranch);
        Assert.Same(second, Assert.IsType<LocalReferenceExpression>(nested.Condition).Local);
        _ = Assert.Single(nested.ThenBranch.Statements);
        _ = Assert.IsType<Block>(nested.ElseBranch);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APlainWriteOfAPossiblyVoidValueEndsTheCoalescingNarrowing(bool fromLocal)
    {
        var x = Local("x", Optional);
        _ = Local("maybe", Optional);
        _ = Bind(AsStatement(Assign("x", "??=", Integer("5"))));
        Assert.Same(Int, Bind(Read("x")).Type);

        _ = Bind(AsStatement(Assign("x", "=", fromLocal ? Read("maybe") : Void())));

        var read = Assert.IsType<LocalReferenceExpression>(Bind(Read("x")));
        Assert.Same(x, read.Local);
        Assert.Same(Optional, read.Type);
        Assert.False(read.IsNarrowed);
    }

    [Theory]
    [InlineData(OwnershipKind.Owned, OwnershipKind.Owned)]
    [InlineData(OwnershipKind.Unrestricted, OwnershipKind.Unrestricted)]
    public void AResultBuiltFromAnOwnedPayloadMakesItsUnannotatedLocalOwned(OwnershipKind payload, OwnershipKind expected)
    {
        var data = HandBuiltModel.Class("Data");
        f.Register(data);
        var result = new ClassType("Result<,>", "Standard", null, null, null);
        result.TypeParametersList.Add(new TypeParameter("T", 0, result.Scope, result));
        result.TypeParametersList.Add(new TypeParameter("E", 1, result.Scope, result));
        f.Register(result);
        _ = Local("data", data, payload);

        var declaration = Assert.IsType<LocalDeclarationStatement>(Bind(new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
            {
                Type = new TypeReferenceNode
                {
                    First = new NamedSingleTypeRefSingleTypeRefNode { Name = BindingFragments.Name("Result"), TypeArgs = BindingFragments.TypeArguments("Data", "String") },
                },
                Name = BindingFragments.Token("r"),
                Initializer = Read("data"),
            },
        }));

        _ = Assert.IsType<ResultConstructionExpression>(declaration.Initializer);
        Assert.Equal(expected, declaration.Local.Ownership);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private LocalVariable Local(string name, IMistType type, OwnershipKind ownership = OwnershipKind.Unrestricted)
    {
        var local = new LocalVariable(name, type) { Ownership = ownership };
        f.Scope.DeclareLocal(local, HandBuiltModel.Node(30), f.Diagnostics);
        return local;
    }

    private Statement Bind(StatementNode node)
    {
        return StatementBinder.BindStatement(node, f.Binding, f.Builder);
    }

    private Expression Bind(ExpressionNode node)
    {
        return ExpressionBinder.BindExpression(node, f.Binding, f.Builder);
    }

    private static ExpressionNode Read(string name)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(name));
    }

    private static StatementNode AsStatement(ExpressionNode expression)
    {
        return new ExpressionStatementStatementNode { ExpressionStatement = new ExpressionStatementNode { Expression = expression } };
    }

    private static StatementNode Declare(string name, ExpressionNode initializer)
    {
        return new VariableDeclarationStatementStatementNode
        {
            VariableDeclaration = new InferredVariableDeclarationVariableDeclarationStatementNode { Name = BindingFragments.Token(name), Initializer = initializer },
        };
    }

    private static ExpressionNode Assign(string local, string op, ExpressionNode value)
    {
        var assignment = Read(local).AsAssignmentOrNull()!;
        assignment.Group0 = new AssignmentNodeGroup0 { Operator = BindingFragments.Token(op), Value = value };
        return new AssignmentFormExpressionNode { Assignment = assignment };
    }

    private static ExpressionNode Integer(string lexeme)
    {
        var literal = BindingFragments.At<IntegerLiteralExpressionPrimaryNode>();
        literal.IntegerLiteral = BindingFragments.Token(lexeme);
        return BindingFragments.Expression(literal);
    }

    private static ExpressionNode Void()
    {
        var node = BindingFragments.At<TypeLiteralExpressionPrimaryNode>();
        node.TypeRef = new TypeReferenceNode { First = new VoidSingleTypeRefSingleTypeRefNode() };
        return BindingFragments.Expression(node);
    }
}