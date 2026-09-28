using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class ExtensionContractFragments
{
    public HandBuiltModel Model { get; } = new();
    public ClassType Integer { get; }
    public ClassType Cell { get; } = HandBuiltModel.Class("Cell");
    public ExtensionBlock Block { get; } = new("self", "Probe", new Scope(null), null);
    public TypeParameter T { get; }
    public Field Counter { get; }

    public ExtensionContractFragments()
    {
        Integer = Model.Type("Int32");
        Integer.Modifiers = new MemberModifiers { IsIntrinsic = true, IsReadonly = true };
        T = new TypeParameter("T", 0, Block.Scope, null);
        Block.TypeParametersList.Add(T);
        Block.ResolvedReceiverType = T;
        Block.Receiver = new LocalVariable("self", T);
        Counter = new Field("Counter", Integer, OwnershipKind.Unrestricted, new MemberModifiers { IsStatic = true });
    }

    public Method Declaration(Block? body, MemberModifiers modifiers = default, List<Parameter>? parameters = null,
        ThisTier? tier = null, OwnershipKind ownership = OwnershipKind.Unrestricted, IMistType? returns = null)
    {
        var method = new Method("Helper", returns ?? Integer, parameters ?? [], modifiers)
        {
            Body = body,
            Scope = new Scope(Block.Scope),
            DeclaringBlock = Block,
            DeclarationNode = HandBuiltModel.Node(11),
            DeclaredThisTier = tier,
            ReturnOwnership = ownership,
        };
        Block.MethodsList.Add(method);
        return method;
    }

    // Analysis receives this hand-built signature view as its input; binder behavior belongs to Tier 1.
    public Method View(Method declaration, IMistType? argument = null)
    {
        var type = argument ?? Integer;
        var method = new Method(declaration.Name, ReferenceEquals(declaration.ReturnType, T) ? type : declaration.ReturnType,
            declaration.ParametersList.ConvertAll(parameter => new Parameter(parameter.Name,
                ReferenceEquals(parameter.Type, T) ? type : parameter.Type, parameter.Modifier)), declaration.Modifiers)
        {
            SourceDeclaration = declaration,
            ClosedBlock = Block,
            DeclaringBlock = Block,
            DeclarationNode = declaration.DeclarationNode,
            DeclaredThisTier = declaration.DeclaredThisTier,
            ReturnOwnership = declaration.ReturnOwnership,
        };
        method.TypeParametersList.AddRange(declaration.TypeParametersList);
        method.ContextBindingsList.AddRange(declaration.ContextBindingsList.Select(binding =>
            new ContextBinding(ReferenceEquals(binding.ContextType, T) ? type : binding.ContextType, binding.Identifier)));
        return method;
    }

    public PurityInference Inference()
    {
        return new PurityInference(Model.Context(new PurityAnalyzer(), new DiagnosticBag(), extensions: [Block]));
    }

    public IReadOnlyList<Diagnostic> ThroughCaller(Method view, MemberModifiers? promise = null,
        OwnershipKind receiverOwnership = OwnershipKind.Unrestricted, params ContextBinding[] contexts)
    {
        var receiver = new LocalVariable("receiver", Cell) { Ownership = receiverOwnership, Parameter = new Parameter("receiver", Cell) };
        var caller = new Method("Caller", view.ReturnType, [receiver.Parameter], promise ?? new MemberModifiers { IsPure = true, IsStatic = true })
        {
            Body = Returning(Call(view, new LocalReferenceExpression(HandBuiltModel.Node(), receiver), 37)),
        };
        caller.ContextBindingsList.AddRange(contexts);
        return Analyze(caller);
    }

    public IReadOnlyList<Diagnostic> Analyze(Method method)
    {
        return Model.Analyze(new PurityAnalyzer(), method.Body, member: method, extensions: [Block]);
    }

    public static Block Returning(Expression value)
    {
        return HandBuiltModel.Block(new ReturnStatement(value, value.Type));
    }

    public LiteralExpression Number()
    {
        return HandBuiltModel.Literal(Integer, 1L);
    }

    public FieldReferenceExpression MutableRead(int offset = 23)
    {
        return new FieldReferenceExpression(HandBuiltModel.Node(offset), Counter);
    }

    public Block Writing(Expression target)
    {
        return HandBuiltModel.Block(new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(23), target, Number(), target.Type)));
    }

    public static InvocationExpression Call(Method method, Expression? receiver = null, int offset = 0)
    {
        Expression target = receiver is null
            ? new MethodReferenceExpression(HandBuiltModel.Node(), method)
            : new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, method.ReturnType);
        return new InvocationExpression(HandBuiltModel.Node(offset), target, [], method.ReturnType, method, []);
    }
}