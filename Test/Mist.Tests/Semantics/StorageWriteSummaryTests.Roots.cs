using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

public sealed partial class StorageWriteSummaryTests
{
    /// <summary>
    /// Neither direction's signature mentions T, so T can only come from the bodies the trial binds out
    /// of the syntax the declaration builder parked; the real binding later finds that parked form again.
    /// </summary>
    [Fact]
    public void AnUnfinishedAccessorIsBoundInTheTrialAndLeftAsItWasParked()
    {
        var box = InSource(new Generic("Box", "T"));
        var getter = new Block(Syntax(Touch(ThisSyntax), Return(Zero())));
        var parkedStatements = getter.Statements;
        var parkedExpression = new UnresolvedExpression(Touch(ThisSyntax));
        var lazy = new Property("Lazy", Int32, DeclarationFragments.Public)
        {
            DeclaringType = box.Type,
            Getter = new PropertyAccessor { Body = getter },
            Setter = new PropertyAccessor { ExpressionBody = parkedExpression },
        };
        box.Type.PropertiesList.Add(lazy);

        Assert.Equal(Set(box.T), Writes(lazy, StorageAccess.Read));
        Assert.Equal(Set(box.T), Writes(lazy, StorageAccess.Write));
        Assert.Same(parkedStatements, getter.Statements);
        Assert.Same(parkedExpression, lazy.Setter.ExpressionBody);
    }

    [Fact]
    public void AFinishedAccessorIsReadAsItStandsAndAnAutoAccessorAnswersFromItsSignature()
    {
        var box = InSource(new Generic("Box", "T"));
        var lazy = box.Property("Lazy", box.T,
            getter: Body(Assign(box.ValueOf(This(box.Type)), box.ValueOf(This(box.Type)))),
            setter: Body(Assign(box.CountOf(This(box.Type)), Local("value", Int32))));
        var bound = lazy.Getter!.Body!.Statements;
        var item = new Property("Item", box.T, DeclarationFragments.Public)
        {
            DeclaringType = box.Type,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        };
        box.Type.PropertiesList.Add(item);
        fragments.Builder.StorageWrites.MarkComplete(lazy);
        fragments.Builder.StorageWrites.MarkComplete(item);

        Assert.Equal(Set(box.T), Writes(lazy, StorageAccess.Read));
        Assert.Empty(Writes(lazy, StorageAccess.Write));
        Assert.Empty(Writes(item, StorageAccess.Read));
        Assert.Equal(Set(box.T), Writes(item, StorageAccess.Write));
        Assert.Same(bound, lazy.Getter.Body.Statements);
    }

    [Fact]
    public void AnUnfinishedExtensionPropertyIsBoundInTheBlocksScope()
    {
        var box = new Generic("Box", "T");
        var block = new ExtensionBlock("self", "Probe", new ExtensionScope(null), new ExtensionBlockDeclarationNode());
        var e = new TypeParameter("E", 0, block.Scope, null);
        block.TypeParametersList.Add(e);
        var receiver = Interner.GetOrCreateConstructedType(box.Type, [e]);
        block.ResolvedReceiverType = receiver;
        block.Receiver = new LocalVariable("self", receiver);
        var peek = new Property("Peek", Int32, DeclarationFragments.Public)
        {
            Getter = new PropertyAccessor { Body = new Block(Syntax(Touch(() => BindingFragments.Identifier("self")), Return(Zero()))) },
        };
        block.PropertiesList.Add(peek);
        fragments.Built.Model.GetOrAddNamespace(block.NamespaceName).Extensions[box.Type] = [block];

        Assert.Equal(Set(e), Writes(peek, StorageAccess.Read));
    }

    private IReadOnlySet<TypeParameter> Writes(object member, StorageAccess side)
    {
        Assert.True(fragments.Builder.StorageWrites.TryWrites(member, side, fragments.Builder, out var writes));
        return writes;
    }

    private Generic InSource(Generic generic)
    {
        generic.Type.Assembly = fragments.Built.Model.AddSelfAssembly("Probe");
        _ = fragments.Built.Model.RegisterType(generic.Type);
        return generic;
    }

    private static PrimaryNode ThisSyntax()
    {
        var node = BindingFragments.At<ThisExpressionPrimaryNode>();
        node.THIS = BindingFragments.Token("this");
        return node;
    }

    private static ExpressionNode Touch(Func<PrimaryNode> receiver)
    {
        var assignment = (AssignmentFormExpressionNode)BindingFragments.Expression(receiver(), ValueAccess());
        assignment.Assignment.Group0 = new AssignmentNodeGroup0
        {
            Operator = BindingFragments.Token("="),
            Value = BindingFragments.Expression(receiver(), ValueAccess()),
        };
        return assignment;
    }

    private static MemberAccessSuffixPostfixSuffixNode ValueAccess()
    {
        var suffix = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        suffix.Name = BindingFragments.Token("Value");
        return suffix;
    }

    private static ExpressionNode Zero()
    {
        return BindingFragments.Expression(new IntegerLiteralExpressionPrimaryNode { IntegerLiteral = BindingFragments.Token("0") });
    }

    private static ReturnStatementStatementNode Return(ExpressionNode value)
    {
        return new ReturnStatementStatementNode { ReturnStatement = new ReturnStatementNode { Value = value } };
    }

    private static BlockNode Syntax(ExpressionNode touch, StatementNode last)
    {
        return new BlockNode
        {
            Statements = [new ExpressionStatementStatementNode { ExpressionStatement = new ExpressionStatementNode { Expression = touch } }, last],
        };
    }
}