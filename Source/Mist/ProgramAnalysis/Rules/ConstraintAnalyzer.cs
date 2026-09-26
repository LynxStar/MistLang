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
/// nothing else checks them at all. Order 6 of the analysis register.
/// </summary>
internal sealed class ConstraintAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var closing in context.Model.ConstructorConstraintClosures.Closings)
        {
            if(closing.Argument is ErrorType || !context.IsInSource(closing.Node))
            {
                continue;
            }

            foreach(var argument in Arguments(closing))
            {
                var judged = closing with { Argument = argument, Closing = closing.Closing.With(closing.Parameter, argument) };

                foreach(var constraint in closing.Parameter.ConstraintsList)
                {
                    Judge(context, judged, constraint);
                }
            }
        }
    }

    private static IEnumerable<IMistType> Arguments(TypeArgumentClosing closing)
    {
        if(!closing.Parameter.IsPack)
        {
            return [closing.Argument];
        }

        return closing.Argument is LiftedType { Value: TypePackValue pack }
            ? pack.Elements.Where(element => element is not (TypeParameter or SpreadType))
            : [];
    }

    private static void Judge(AnalysisContext context, TypeArgumentClosing closing, TypeParameterConstraint declared)
    {
        if(Closed(declared, closing.Closing, context.Model.TypeInterner) is not { } constraint)
        {
            return;
        }

        var argument = closing.Argument;
        var definition = argument is ConstructedType constructed ? constructed.Definition : argument;

        var satisfied = constraint switch
        {
            TypeConstraint typed => ConversionClassifier.Classify(argument, typed.ConstraintType, context.Model.TypeInterner)
                is ConversionKind.Identity or ConversionKind.Implicit or ConversionKind.Unclassified,
            ConstructorConstraint constructor => HasConstructor(argument, constructor.ParameterTypes, context.Model.TypeInterner),
            KeywordConstraint keyword => Keyword(keyword.Kind, argument, definition),
            _ => true,
        };

        if(satisfied)
        {
            return;
        }

        if(constraint is ConstructorConstraint required)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ConstructorConstraintNotSatisfied, closing.Node, [],
                TypeDisplay.Mist(argument), string.Join(", ", required.ParameterTypes.Select(TypeDisplay.Mist)), closing.Parameter.Name);
        }
        else
        {
            context.Report(
                ProgramAnalysisDiagnostics.ConstraintNotSatisfied, closing.Node, [],
                TypeDisplay.Mist(argument), Spelling(constraint), closing.Parameter.Name);
        }
    }

    /// <summary>
    /// Null when the closing put a type that already failed into the constraint, as a sibling argument
    /// that did not resolve does: that type was reported where it was written.
    /// </summary>
    private static TypeParameterConstraint? Closed(
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
            return true;
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