using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Where a value meets a closing that no proof site of its own visits, judged at the step the flow
/// evaluates the value: a switch arm's result, the right of <c>??</c>, an array literal's element, an
/// expression-bodied lambda's result, a with block's value, and a field or property initializer or a
/// getter's expression body, each of which is a body root. Also which closing each capture fills, so a
/// contract naming a sibling slot reads the sibling from the closing's own argument for it.
/// </summary>
internal sealed class ClosingSites
{
    public static readonly ClosingSites None = new();

    private readonly Dictionary<Expression, IMistType> targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SlotCapture, IReadOnlyDictionary<TypeParameter, IMistType>> closings = new(ReferenceEqualityComparer.Instance);

    public static ClosingSites Of(AnalysisContext context)
    {
        var sites = new ClosingSites();

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case SwitchExpression switched:
                    foreach(var arm in switched.Arms)
                    {
                        sites.targets[arm.Result] = switched.Type;
                    }

                    break;

                case CoalesceExpression coalesce:
                    sites.targets[coalesce.Right] = coalesce.Type;
                    break;

                case ArrayLiteralExpression literal:
                    foreach(var element in literal.Elements)
                    {
                        sites.targets[element] = literal.ElementType;
                    }

                    break;

                case LambdaExpression { Result: { } result } lambda when ResultTarget(lambda) is { } target:
                    sites.targets[result] = target;
                    break;

                case WithBlockStatement ambient when !ReferenceEquals(ambient.KeyType, ambient.Value.Type):
                    sites.targets[ambient.Value] = ambient.KeyType;
                    break;
            }

            foreach(var capture in SlotCapture.Of(node))
            {
                if(ClosingOf(node, capture) is { } closing)
                {
                    sites.closings[capture] = closing;
                }
            }
        }

        foreach(var root in context.Bodies)
        {
            if(root.Root is Expression expression && Declared(root) is { } declared)
            {
                sites.targets[expression] = declared;
            }
        }

        return sites;
    }

    public IMistType? TargetOf(Expression value)
    {
        return targets.GetValueOrDefault(value);
    }

    public IReadOnlyDictionary<TypeParameter, IMistType>? ClosingOf(SlotCapture capture)
    {
        return closings.GetValueOrDefault(capture);
    }

    public static IMistType? ResultTarget(LambdaExpression lambda)
    {
        if(lambda.Type is not IFunctionType function)
        {
            return null;
        }

        var declared = lambda.IsAsync ? TaskResult.Of(function.ReturnType) ?? function.ReturnType : function.ReturnType;

        return OptionalUnion.IsVoid(declared) ? null : declared;
    }

    private static IMistType? Declared(SourceRoot root)
    {
        return root.Member switch
        {
            Field field when ReferenceEquals(root.Root, field.Initializer) => field.Type,
            Property property when ReferenceEquals(root.Root, property.Initializer) || ReferenceEquals(root.Root, property.Getter?.ExpressionBody) => property.Type,
            Indexer indexer when ReferenceEquals(root.Root, indexer.Getter?.ExpressionBody) => indexer.Type,
            _ => null,
        };
    }

    private static IReadOnlyDictionary<TypeParameter, IMistType>? ClosingOf(object node, SlotCapture capture)
    {
        foreach(var (parameters, arguments) in Closings(node))
        {
            if(arguments.Any(argument => ReferenceEquals(argument, capture.Value)))
            {
                return parameters.Zip(arguments).ToDictionary(pair => pair.First, pair => pair.Second, ReferenceEqualityComparer.Instance as IEqualityComparer<TypeParameter>);
            }
        }

        return null;
    }

    private static IEnumerable<(IReadOnlyList<TypeParameter> Parameters, IReadOnlyList<IMistType> Arguments)> Closings(object node)
    {
        IEnumerable<IMistType> written = node switch
        {
            LocalDeclarationStatement declaration => [declaration.Local.Type],
            NewExpression creation => [creation.Type],
            ArrayCreationExpression array => [array.Type],
            StackallocExpression stack => [stack.Type],
            TypeTestPattern test => [test.NarrowedType],
            CastExpression cast => [cast.TargetType],
            AsExpression conversion => [conversion.TargetType],
            MemberAccessExpression { Receiver: TypeReferenceExpression typed } => [typed.ReferencedType],
            InvocationExpression call => [.. call.ParameterTypes ?? [], .. (call.Receiver as MemberAccessExpression)?.TypeArguments ?? []],
            _ => [],
        };

        if(node is InvocationExpression { Method: { TypeParametersList.Count: > 0 } method, Receiver: MemberAccessExpression { TypeArguments.Count: > 0 } receiver }
            && method.TypeParametersList.Count == receiver.TypeArguments.Count)
        {
            yield return (method.TypeParametersList, receiver.TypeArguments);
        }

        foreach(var type in written)
        {
            foreach(var closing in Constructed(type))
            {
                yield return closing;
            }
        }
    }

    private static IEnumerable<(IReadOnlyList<TypeParameter>, IReadOnlyList<IMistType>)> Constructed(IMistType type)
    {
        switch(type)
        {
            case ConstructedType { Definition: IHasTypeParameters declared } constructed when declared.TypeParametersList.Count == constructed.TypeArguments.Count:
                yield return (declared.TypeParametersList, constructed.TypeArguments);
                foreach(var argument in constructed.TypeArguments.SelectMany(Constructed))
                {
                    yield return argument;
                }

                break;

            case UnionType union:
                foreach(var constituent in union.Constituents.SelectMany(Constructed))
                {
                    yield return constituent;
                }

                break;

            case RefinedType refined:
                foreach(var beneath in Constructed(refined.BaseType))
                {
                    yield return beneath;
                }

                break;
        }
    }
}