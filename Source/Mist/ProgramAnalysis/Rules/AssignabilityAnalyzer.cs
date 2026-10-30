using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A value at a position that expects a type it cannot implicitly become: one question, <see cref="Assignability"/>, asked at every position the binder gave a
/// target type to, and reported at the value, since the value is what has to change. An argument is
/// judged against what <see cref="ArgumentTargets"/> pairs it with, a CLR-projected callee's included,
/// and an index against the indexer it selected, an intrinsic or projected one included.
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

                case AssignmentExpression assignment when StorageLocations.NotAssignable(assignment.Target) is null:
                    Check(context, assignment.Value, AssignedType(context, assignment), assignment.Target);
                    break;

                case ReturnStatement { Value: { } value, TargetType: { } target }:
                    Check(context, value, target);
                    break;

                case InvocationExpression call:
                    CheckArguments(context, call.Arguments, call.ParameterTypes, call.Method, call.Node);
                    break;

                case NewExpression creation:
                    CheckConstruction(context, creation);
                    break;

                case IndexExpression { ParameterType: { } parameter } index:
                    Check(context, index.Index, parameter);
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

                case WithBlockStatement ambient when !ReferenceEquals(ambient.KeyType, ambient.Value.Type):
                    Check(context, ambient.Value, ambient.KeyType);
                    break;

                case ImplicitConversionExpression conversion when Assignability.JudgeSelection(conversion.AmbiguousOperators.Count) is { } ambiguity:
                    context.Report(
                        ambiguity,
                        conversion.Node,
                        [conversion, conversion.Operand],
                        DiagnosticSnapshot.Type(conversion.Operand.Type),
                        DiagnosticSnapshot.Type(conversion.TargetType),
                        new SequenceValue(SlotKind.Symbol, [.. conversion.AmbiguousOperators.Select(candidate => DiagnosticSnapshot.Symbol(candidate, CastAnalyzer.Signature(candidate)))]));
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
            CheckArguments(
                context, constructor.ChainArgumentsList, constructor.ChainParameterTypes, constructor.ChainedConstructor,
                constructor.DeclarationNode);
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
        AnalysisContext context, IReadOnlyList<Expression> arguments, IReadOnlyList<IMistType>? parameters, Method? callee, SyntaxNode? call)
    {
        if(parameters is null)
        {
            return;
        }

        var interner = context.Model.TypeInterner;
        var passed = arguments.Select(argument => argument is SpreadExpression ? null : argument.Type).ToList();

        if(ArgumentTargets.Unpaired(callee?.ParametersList, parameters, passed, interner) is { } slot)
        {
            var absorbed = arguments.Skip(slot.First).Take(slot.Count).ToList();

            if((absorbed.FirstOrDefault()?.Node ?? call) is { } at)
            {
                Check(context, slot.From, slot.To, at, absorbed);
            }
        }

        if(ArgumentTargets.Of(callee?.ParametersList, parameters, passed, interner) is not { } targets)
        {
            return;
        }

        for(var i = 0; i < arguments.Count; i++)
        {
            if(arguments[i] is not SpreadExpression && targets[i] is { } target)
            {
                Check(context, arguments[i], target);
            }
        }
    }

    private static void CheckConstruction(AnalysisContext context, NewExpression creation)
    {
        CheckArguments(context, creation.Arguments, creation.ParameterTypes, creation.Constructor, creation.Node);

        foreach(var entry in creation.Initializer)
        {
            if(entry.TargetType is { } target)
            {
                Check(context, entry.Value, target);
            }
            else if(entry.Add is { } add)
            {
                CheckArguments(context, [entry.Value], entry.AddParameterTypes, add, entry.Node);
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
        var interner = context.Model.TypeInterner;

        if((Assignability.Judge(value, target, interner) ?? Assignability.Boxing(value.Type, target, interner)) is { } descriptor
            && !UnheldCopies.Report(context, value.Type, target, value.Node, [value, .. alsoAbout]))
        {
            context.Report(descriptor, value.Node, [value, .. alsoAbout], DiagnosticSnapshot.Type(value.Type), DiagnosticSnapshot.Type(target));
        }
    }

    private static void Check(
        AnalysisContext context, IMistType from, IMistType to, SyntaxNode at, IReadOnlyList<Expression> about)
    {
        var interner = context.Model.TypeInterner;

        if((Assignability.Judge(from, to, interner) ?? Assignability.Boxing(from, to, interner)) is { } descriptor
            && !UnheldCopies.Report(context, from, to, at, about))
        {
            context.Report(descriptor, at, about, DiagnosticSnapshot.Type(from), DiagnosticSnapshot.Type(to));
        }
    }
}