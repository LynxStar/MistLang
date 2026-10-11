using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A type argument against the constraints of the parameter it closes, at every closing written or
/// inferred in the project's source. The nominal kinds Roslyn would refuse against the generated file;
/// the Mist-only kinds -- readonly, mutable, owned, the substructural three, default -- erase, so
/// nothing else checks them at all. An argument that is itself a type parameter is judged by
/// <see cref="ConstraintEntailment"/> against what it declares, at the site that forwards it; a pack
/// forwarded whole, spread into a pack, or one of its elements, is judged by what that pack declares of
/// every element.
/// </summary>
internal sealed class ConstraintAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        var closures = context.Model.ConstructorConstraintClosures;

        foreach(var closing in closures.Closings.Concat(closures.OpenClosings))
        {
            if(closing.Argument is ErrorType || !context.IsInSource(closing.Node))
            {
                continue;
            }

            if(!FillsDeclaredLength(closing.Parameter, closing.Argument))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ConstraintNotSatisfied, closing.Node, [],
                    TypeDisplay.Mist(closing.Argument), $"Type[{closing.Parameter.PackLength}]", closing.Parameter.Name);
            }

            foreach(var constraint in closing.Parameter.ConstraintsList)
            {
                foreach(var argument in Arguments(closing, constraint))
                {
                    var judged = closing with { Argument = argument, Closing = closing.Closing.With(closing.Parameter, argument) };
                    Judge(context, judged, constraint);
                }
            }
        }
    }

    /// <summary>
    /// Whether a pack declared <c>Type[N]</c> is closed with exactly N types, a spread among them
    /// counting for the fixed size of the pack it spreads and proving nothing when that pack has none.
    /// Judged at every closing because indexing the pack in a type position (variadics Requirement 6.5)
    /// is held to the declared N alone, once, where the index is written.
    /// </summary>
    internal static bool FillsDeclaredLength(TypeParameter parameter, IMistType argument)
    {
        if(parameter.PackLength is not { } declared || argument is not LiftedType { Value: TypePackValue pack })
        {
            return true;
        }

        var given = 0;

        foreach(var element in pack.Elements)
        {
            if(element is not SpreadType spread)
            {
                given++;
                continue;
            }

            if(spread.Operand is not TypeParameter { PackLength: { } spliced })
            {
                return false;
            }

            given += spliced;
        }

        return given == declared;
    }

    private static IEnumerable<IMistType> Arguments(TypeArgumentClosing closing, TypeParameterConstraint constraint)
    {
        if(!closing.Parameter.IsPack)
        {
            return [closing.Argument];
        }

        if(constraint.Kind == TypeParameterConstraintKind.Stampable)
        {
            return closing.Argument is LiftedType { Value: TypePackValue pack }
                ? pack.Elements.Select(element => element is SpreadType spread ? spread.Operand : element)
                : [closing.Argument];
        }

        return closing.Argument switch
        {
            LiftedType { Value: TypePackValue otherPack } => otherPack.Elements.SelectMany(element => element switch
            {
                SpreadType { Operand: TupleType tuple } => tuple.ElementTypes,
                SpreadType spread => [spread.Operand],
                _ => [element],
            }),
            TypeParameter whole => [whole],
            _ => [],
        };
    }

    private static void Judge(AnalysisContext context, TypeArgumentClosing closing, TypeParameterConstraint declared)
    {
        var decision = closing.Argument is TypeParameter
            ? ConstraintEntailment.Decide(closing, declared, context.Model.TypeInterner)
            : Decide(closing, declared, context.Model.TypeInterner);

        if(decision is not { } descriptor
            || Closed(declared, closing.Closing, context.Model.TypeInterner) is not { } constraint)
        {
            return;
        }

        var argument = closing.Argument;

        if(constraint is ConstructorConstraint required)
        {
            context.Report(
                descriptor, closing.Node, [],
                TypeDisplay.Mist(argument), string.Join(", ", required.ParameterTypes.Select(TypeDisplay.Mist)), closing.Parameter.Name);
        }
        else
        {
            context.Report(
                descriptor, closing.Node, [],
                TypeDisplay.Mist(argument), Spelling(constraint), closing.Parameter.Name);
        }
    }

    internal static DiagnosticDescriptor? Decide(TypeArgumentClosing closing, TypeParameterConstraint declared, TypeInterner interner)
    {
        if(closing.Argument is ErrorType || Closed(declared, closing.Closing, interner) is not { } constraint)
        {
            return null;
        }

        var argument = closing.Argument;
        var definition = argument is ConstructedType constructed ? constructed.Definition : argument;

        var satisfied = constraint switch
        {
            TypeConstraint typed => Is(argument, typed.ConstraintType, interner),
            ConstructorConstraint constructor => HasConstructor(argument, constructor.ParameterTypes, interner),
            KeywordConstraint keyword => Keyword(keyword.Kind, argument, definition),
            _ => true,
        };

        if(satisfied)
        {
            return null;
        }

        return constraint is ConstructorConstraint
            ? ProgramAnalysisDiagnostics.ConstructorConstraintNotSatisfied
            : ProgramAnalysisDiagnostics.ConstraintNotSatisfied;
    }

    /// <summary>
    /// A type constraint asks what the argument IS (generic-constraints spec), so an operator conversion
    /// satisfies nothing.
    /// </summary>
    private static bool Is(IMistType argument, IMistType constraint, TypeInterner interner)
    {
        return ConversionClassifier.Classify(argument, constraint, interner) switch
        {
            ConversionKind.Identity or ConversionKind.Unclassified => true,
            ConversionKind.Implicit => ConversionClassifier.IsFree(argument, constraint, interner) && !ConversionRelation.CopiesAClone(argument, constraint),
            _ => false,
        };
    }

    /// <summary>
    /// Null when the closing put a type that already failed into the constraint, as a sibling argument
    /// that did not resolve does: that type was reported where it was written.
    /// </summary>
    internal static TypeParameterConstraint? Closed(
        TypeParameterConstraint declared, TypeSubstitution closing, TypeInterner interner)
    {
        switch(declared)
        {
            case TypeConstraint typed:
                var required = closing.Apply(typed.ConstraintType, interner);
                return Failed(required) ? null : new TypeConstraint(required);

            case ConstructorConstraint constructor:
                var signature = constructor.ParameterTypes.Select(type => closing.Apply(type, interner)).ToList();
                return signature.Exists(Failed) ? null : new ConstructorConstraint(signature);

            default:
                return declared;
        }
    }

    private static bool Failed(IMistType type)
    {
        return type switch
        {
            ErrorType => true,
            ConstructedType constructed => constructed.TypeArguments.Any(Failed),
            UnionType union => union.Constituents.Any(Failed),
            TupleType tuple => tuple.ElementTypes.Any(Failed),
            FunctionType function => function.ParameterTypes.Any(Failed) || Failed(function.ReturnType),
            _ => false,
        };
    }

    private static bool Keyword(TypeParameterConstraintKind kind, IMistType argument, IMistType definition)
    {
        return kind switch
        {
            TypeParameterConstraintKind.Class => definition is not (StructType or EnumType),
            TypeParameterConstraintKind.Struct => definition is not ClassType,
            TypeParameterConstraintKind.Stampable => argument is TypeParameter { IsPack: true } pack
                ? pack.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable)
                : Stampability.IsStampable(argument),
            TypeParameterConstraintKind.Readonly => definition is not MemberType || Declarations.IsReadonly(definition),
            TypeParameterConstraintKind.Mutable => !Declarations.IsReadonly(definition),
            TypeParameterConstraintKind.Owned => definition is not MemberType member || member.Modifiers.IsOwned,
            TypeParameterConstraintKind.Linear => Discipline(definition, SubstructuralDiscipline.Linear),
            TypeParameterConstraintKind.Affine => Discipline(definition, SubstructuralDiscipline.Affine),
            TypeParameterConstraintKind.Relevant => Discipline(definition, SubstructuralDiscipline.Relevant),
            TypeParameterConstraintKind.Default => Defaultability.HasDefault(argument),
            _ => true,
        };
    }

    private static bool Discipline(IMistType definition, SubstructuralDiscipline required)
    {
        return definition is not MemberType member
            || member.Modifiers.Discipline == required
            || member.Modifiers.Discipline == SubstructuralDiscipline.Linear;
    }

    private static bool HasConstructor(IMistType argument, IReadOnlyList<IMistType> signature, TypeInterner interner)
    {
        var definition = argument is ConstructedType constructed ? constructed.Definition : argument;

        if(definition is not ConcreteType concrete)
        {
            return true;
        }

        var substitution = TypeSubstitution.ForReceiver(argument as ConstructedType);

        if(signature.Count == 0 && !Defaultability.NeedsSentinel(concrete))
        {
            return Defaultability.HasImplicitConstructor(argument);
        }

        return concrete.ConstructorsList.Exists(constructor =>
            !constructor.Modifiers.IsStatic
            && constructor.Modifiers.Accessibility == Accessibility.Public
            && constructor.ParametersList.Count == signature.Count
            && constructor.ParametersList.Select((parameter, i) =>
                ConversionRelation.IsIdentity(substitution.Apply(parameter.Type, interner), signature[i])).All(same => same));
    }

    private static string Spelling(TypeParameterConstraint constraint)
    {
        return constraint switch
        {
            TypeConstraint typed => TypeDisplay.Mist(typed.ConstraintType),
            KeywordConstraint keyword => keyword.Kind.ToString().ToLowerInvariant(),
            _ => constraint.Kind.ToString().ToLowerInvariant(),
        };
    }
}