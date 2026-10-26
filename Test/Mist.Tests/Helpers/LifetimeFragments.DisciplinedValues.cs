using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
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

internal sealed partial class LifetimeFragments
{
    public IMistType DisciplinedValue(string shape, SubstructuralDiscipline discipline)
    {
        ConcreteType definition = shape == "class" ? HandBuiltModel.Class("Value") : HandBuiltModel.Struct("Value");
        definition.Modifiers = new MemberModifiers { Discipline = discipline, IsRef = shape == "ref-struct" };
        definition.MethodsList.Add(new Method("Drop", Void, [], default) { DeclaringType = definition });
        definition.FieldsList.Add(new Field("Count", Int32, OwnershipKind.Unrestricted, default) { DeclaringType = definition });

        if(shape == "constructed-struct")
        {
            definition.TypeParametersList.Add(new TypeParameter("T", 0, definition.Scope, definition));
        }

        return shape == "constructed-struct" ? new ConstructedType(definition, [Int32]) : definition;
    }

    public IReadOnlyList<Diagnostic> AnalyzeMoves(Block body)
    {
        var diagnostics = new DiagnosticBag();
        var context = Model.Context(new OwnershipAnalyzer(), diagnostics, body);
        var declarations = BodyWalker.DescendantsAndSelf(body).OfType<Block>()
            .SelectMany(block => block.Statements.OfType<LocalDeclarationStatement>().Select(declaration => (declaration.Local, Block: block)))
            .ToDictionary(pair => pair.Local, pair => pair.Block);
        var parents = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        foreach(var parent in BodyWalker.DescendantsAndSelf(body))
        {
            foreach(var child in BodyWalker.ChildrenOf(parent))
            {
                parents[child] = parent;
            }
        }

        new OwnershipFlow(context, new ThisTiers(), declarations, new Dictionary<Block, LocalReferenceExpression>(), parents, owning: null)
            .Judge(FlowGraphBuilder.Build(body), OwnershipFlow.Nothing);
        return diagnostics.ToImmutableArray();
    }

    public static NewExpression Fresh(IMistType type)
    {
        return new NewExpression(HandBuiltModel.Node(), type, [], null, [], []);
    }

    public MemberAccessExpression Count(LocalVariable local, int offset = 37)
    {
        var definition = (ConcreteType)(local.Type is ConstructedType constructed ? constructed.Definition : local.Type);
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
        var field = definition.FieldsList.Single(field => field.Name == "Count");
        return new MemberAccessExpression(HandBuiltModel.Node(offset), receiver, field.Name, Int32, member: new FieldResult(field, definition, []));
    }

    public Statement MoveValue(string operation, LocalVariable source, LocalVariable destination)
    {
        var value = Ref(source);
        switch(operation)
        {
            case "initialize":
                return Declare(destination, value);
            case "assign":
                return Run(Assign(destination, value));
            case "owned-argument":
                return Run(Call(new Method("Take", Void, [Owned("value", source.Type)], new MemberModifiers { IsStatic = true }) { DeclaringType = Sink }, value));
            case "owned-receiver":
                var consume = new Method("Consume", Void, [], default) { DeclaredThisTier = ThisTier.Owned };
                return Run(new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), value, consume.Name, Void), [], Void, consume, []));
            case "owned-field":
                return Run(Store(new Field("Kept", source.Type, OwnershipKind.Owned, default) { DeclaringType = Sink }, value));
            case "return":
                return Return(value);
            case "drop":
                return Run(Drop(source));
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }
}