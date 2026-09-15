using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A value at a position that expects a type it cannot implicitly become. Order 5 of the analysis
/// register: one question, <see cref="Assignability"/>, asked at every position the binder gave a
/// target type to, and reported at the value, since the value is what has to change. An argument is
/// judged only where it pairs with a parameter by position: not across a flattened pack slot, which
/// absorbs several, and not on a CLR-projected member, whose optional parameters the model does not
/// represent yet, so the overload it names can be one Roslyn will not choose.
/// </summary>
internal sealed class AssignabilityAnalyzer : IProgramAnalyzer
{
    public string Id => "assignability-core";

    public void Analyze(AnalysisContext context)
    {
        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case LocalDeclarationStatement { Initializer: { } initializer } local:
                    Check(context, initializer, local.Local.Type);
                    break;

                case AssignmentExpression assignment:
                    Check(context, assignment.Value, AssignedType(context, assignment), assignment.Target);
                    break;

                case ReturnStatement { Value: { } value, TargetType: { } target } when !OptionalUnion.IsVoid(target):
                    Check(context, value, target);
                    break;

                case InvocationExpression call:
                    CheckArguments(context, call.Arguments, call.ParameterTypes, call.Method);
                    break;

                case NewExpression creation:
                    CheckConstruction(context, creation);
                    break;

                case CoalesceExpression coalesce:
                    Check(context, coalesce.Right, coalesce.Type);
                    break;

                case SwitchExpression switchExpression:
                    foreach(var arm in switchExpression.Arms)
                    {
                        Check(context, arm.Result, switchExpression.Type);
                    }

                    break;

                case ArrayLiteralExpression literal:
                    foreach(var element in literal.Elements)
                    {
                        Check(context, element, literal.ElementType);
                    }

                    break;

                case LambdaExpression { Result: { } result } lambda when ResultTarget(lambda) is { } target:
                    Check(context, result, target);
                    break;

                case ForEachStatement { ElementConversion: null } loop when EnumerableElement.Of(loop.Collection.Type) is { } element:
                    Check(context, element, loop.Variable.Type, loop.Collection.Node, [loop.Collection]);
                    break;

                case TupleDeconstructionStatement deconstruction:
                    CheckDeconstruction(context, deconstruction);
                    break;

                case ImplicitConversionExpression { AmbiguousOperators.Count: > 0 } conversion:
                    context.Report(
                        ProgramAnalysisDiagnostics.AmbiguousImplicitConversion,
                        conversion.Node,
                        [conversion, conversion.Operand],
                        TypeDisplay.Mist(conversion.Operand.Type),
                        TypeDisplay.Mist(conversion.TargetType),
                        string.Join(", ", conversion.AmbiguousOperators.Select(CastAnalyzer.Signature)));
                    break;
            }
        }

        foreach(var type in context.Types)
        {
            CheckDeclarations(context, type);
        }

        foreach(var block in context.Extensions)
        {
            foreach(var property in block.PropertiesList)
            {
                CheckGetter(context, property.Getter, property.Type);
            }
        }
    }

    private static void CheckDeclarations(AnalysisContext context, MistType type)
    {
        if(type is EnumType { UnderlyingType: { } underlying } enumType)
        {
            foreach(var variant in enumType.MembersList)
            {
                if(variant.Value is { } value)
                {
                    Check(context, value, underlying);
                }
            }
        }

        if(type is not MemberType member)
        {
            return;
        }

        foreach(var property in member.PropertiesList)
        {
            if(property.Initializer is { } initializer)
            {
                Check(context, initializer, property.Type);
            }

            CheckGetter(context, property.Getter, property.Type);
        }

        foreach(var indexer in member.IndexersList)
        {
            CheckGetter(context, indexer.Getter, indexer.Type);
        }

        foreach(var field in (member as ConcreteType)?.FieldsList ?? [])
        {
            if(field.Initializer is { } initializer)
            {
                Check(context, initializer, field.Type);
            }
        }

        foreach(var constructor in (member as ConcreteType)?.ConstructorsList ?? [])
        {
            CheckArguments(context, constructor.ChainArgumentsList, constructor.ChainParameterTypes, constructor.ChainedConstructor);
        }
    }

    private static void CheckGetter(AnalysisContext context, PropertyAccessor? getter, IMistType type)
    {
        if(getter?.ExpressionBody is { } body)
        {
            Check(context, body, type);
        }
    }

    private static void CheckArguments(
        AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType>? parameters, Method? callee)
    {
        if(parameters is null || parameters.Count != arguments.Count || AbsorbsArguments(callee))
        {
            return;
        }

        for(var i = 0; i < arguments.Count; i++)
        {
            if(arguments[i] is not SpreadExpression)
            {
                Check(context, arguments[i], parameters[i]);
            }
        }
    }

    private static bool AbsorbsArguments(Method? method)
    {
        return method is not null
            && (method.Modifiers.IsClrProjected || ValuePack.FlattenedSlot(method.ParametersList) is not null);
    }

    private static void CheckConstruction(AnalysisContext context, NewExpression creation)
    {
        CheckArguments(context, creation.Arguments, creation.ParameterTypes, creation.Constructor);

        foreach(var entry in creation.Initializer)
        {
            if(entry.TargetType is { } target)
            {
                Check(context, entry.Value, target);
            }
        }
    }

    private static void CheckDeconstruction(AnalysisContext context, TupleDeconstructionStatement deconstruction)
    {
        if(Elements(deconstruction.Initializer.Type) is not { } elements || elements.Count != deconstruction.Targets.Count)
        {
            return;
        }

        for(var i = 0; i < elements.Count; i++)
        {
            if(deconstruction.ElementConversions[i] is not null)
            {
                continue;
            }

            Check(
                context, elements[i], deconstruction.Targets[i].Type,
                deconstruction.Initializer.Node, [deconstruction.Initializer]);
        }
    }

    private static IReadOnlyList<IMistType>? Elements(IMistType type)
    {
        return type switch
        {
            INamedTupleType named => named.Underlying.ElementTypes,
            ITupleType tuple => tuple.ElementTypes,
            _ => null,
        };
    }

    private static IMistType AssignedType(AnalysisContext context, AssignmentExpression assignment)
    {
        return assignment.IsCoalescing
            ? OptionalLifting.NonVoidPortion(assignment.Target.Type, context.Model.TypeInterner)
            : assignment.Target.Type;
    }

    private static IMistType? ResultTarget(LambdaExpression lambda)
    {
        if(lambda.Type is not IFunctionType function)
        {
            return null;
        }

        var declared = lambda.IsAsync ? TaskResult.Of(function.ReturnType) ?? function.ReturnType : function.ReturnType;

        return OptionalUnion.IsVoid(declared) ? null : declared;
    }

    private static void Check(AnalysisContext context, Expression value, IMistType target, params Expression[] alsoAbout)
    {
        if(Assignability.Judge(value, target, context.Model.TypeInterner) is { } descriptor)
        {
            context.Report(descriptor, value.Node, [value, .. alsoAbout], TypeDisplay.Mist(value.Type), TypeDisplay.Mist(target));
        }
    }

    private static void Check(
        AnalysisContext context, IMistType from, IMistType to, SyntaxNode at, IReadOnlyList<Expression> about)
    {
        if(Assignability.Judge(from, to, context.Model.TypeInterner) is { } descriptor)
        {
            context.Report(descriptor, at, about, TypeDisplay.Mist(from), TypeDisplay.Mist(to));
        }
    }
}