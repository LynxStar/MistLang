using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Refusals decidable from a declaration alone: a base or a newtype source that is sealed, `sealed` on
/// an alias, an enum base that is not readonly, two variants sharing a value, a default block
/// naming something that is not a field, and an operator that is not public, is declared without its
/// pair, is a logical operator off Boolean, or takes no parameter of the type declaring it, and a
/// refinement predicate -- an alias's, a parameter's `where` or a dependent parameter's -- that orders
/// a type <see cref="Comparability"/> gives no order. Order 6 of the analysis register; each shape is
/// classic's verdict, measured, or the spec's where classic dropped the declaration unread, and the
/// enum and sealed rules are the full-accessibility-model and enum-readonly-restriction specs' own.
/// </summary>
internal sealed class DeclarationShapeAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types)
        {
            CheckRefinements(context, type);

            switch(type)
            {
                case ConcreteType { CloneSource: { } source, NewtypeDeclaration: { } newtypeDeclaration }:
                    if(Declarations.IsSealed(source))
                    {
                        Report(context, newtypeDeclaration, ProgramAnalysisDiagnostics.NewtypeOverSealedType, TypeDisplay.Mist(source));
                    }

                    break;

                case ConcreteType { BaseType: { } baseType } concrete:
                    if(Declarations.IsSealed(baseType))
                    {
                        Report(context, concrete.DeclarationNode, ProgramAnalysisDiagnostics.SealedBaseType, TypeDisplay.Mist(baseType));
                    }

                    CheckDefaults(context, concrete);
                    CheckOperators(context, concrete);
                    break;

                case ConcreteType concrete:
                    CheckDefaults(context, concrete);
                    CheckOperators(context, concrete);
                    break;

                case Newtype { SourceType: { } source } newtype when Declarations.IsSealed(source):
                    Report(context, newtype.DeclarationNode, ProgramAnalysisDiagnostics.NewtypeOverSealedType, TypeDisplay.Mist(source));
                    break;

                case TypeAlias { Modifiers.IsSealed: true } alias:
                    Report(context, alias.DeclarationNode, ProgramAnalysisDiagnostics.SealedTypeAlias);
                    break;

                case EnumType enumType:
                    CheckEnum(context, enumType);
                    break;
            }
        }
    }

    private static void CheckEnum(AnalysisContext context, EnumType enumType)
    {
        if(enumType.UnderlyingType is { } underlying)
        {
            var constituents = underlying is UnionType union ? union.Constituents : [underlying];

            foreach(var constituent in constituents.Where(constituent => !Declarations.IsReadonly(constituent) && constituent is not ErrorType))
            {
                Report(context, enumType.DeclarationNode, ProgramAnalysisDiagnostics.EnumBaseNotReadonly, enumType.Name, TypeDisplay.Mist(constituent));
            }
        }

        var seen = new Dictionary<object, string>();

        foreach(var variant in enumType.MembersList)
        {
            if(Constant(variant.Value) is not { } value)
            {
                continue;
            }

            if(seen.TryGetValue(value, out var first))
            {
                Report(
                    context, (SyntaxNode?)variant.DeclarationNode ?? enumType.DeclarationNode,
                    ProgramAnalysisDiagnostics.EnumDuplicateValue, variant.Name, first, enumType.Name);
            }
            else
            {
                seen[value] = variant.Name;
            }
        }
    }

    private static object? Constant(Expression? value)
    {
        return value switch
        {
            LiteralExpression { Value: long constant } => constant,
            LiteralExpression { Value: string text } => text,
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression { Value: long constant } } => -constant,
            _ => null,
        };
    }

    private static void CheckDefaults(AnalysisContext context, ConcreteType type)
    {
        if(type.DefaultBlock is { } block)
        {
            CheckEntries(context, type, block.Entries);
        }
    }

    private static void CheckEntries(AnalysisContext context, ConcreteType type, IReadOnlyList<DefaultEntry> entries)
    {
        foreach(var entry in entries)
        {
            if(!type.FieldsList.Exists(field => field.Name == entry.FieldName))
            {
                var at = entry switch
                {
                    DefaultValueEntry valued => valued.Value.Node,
                    DefaultBindingEntry binding => binding.BindingNode,
                    _ => null,
                };

                Report(context, at, ProgramAnalysisDiagnostics.DefaultEntryNotAField, entry.FieldName, type.Name);
                continue;
            }

            if(entry is DefaultBindingEntry { BoundType: ConcreteType bound } nested)
            {
                CheckEntries(context, bound, nested.NestedEntries);
            }
        }
    }

    private static readonly Dictionary<OperatorKind, OperatorKind> Complements = new()
    {
        [OperatorKind.EqualEqual] = OperatorKind.NotEqual,
        [OperatorKind.NotEqual] = OperatorKind.EqualEqual,
        [OperatorKind.LessThan] = OperatorKind.GreaterThan,
        [OperatorKind.GreaterThan] = OperatorKind.LessThan,
        [OperatorKind.LessThanOrEqual] = OperatorKind.GreaterThanOrEqual,
        [OperatorKind.GreaterThanOrEqual] = OperatorKind.LessThanOrEqual,
    };

    private static void CheckOperators(AnalysisContext context, ConcreteType type)
    {
        foreach(var op in type.OperatorsList)
        {
            if(op.Modifiers.Accessibility != Accessibility.Public)
            {
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorNotPublic, op.Symbol);
            }

            if(op.Kind is OperatorKind.LogicalAnd or OperatorKind.LogicalOr
                && type.FullyQualifiedName != WellKnownTypes.Boolean.FullyQualifiedName)
            {
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.LogicalOperatorDeclared, op.Symbol);
                continue;
            }

            if(!op.ParametersList.Exists(parameter => Names(parameter.Type, type)))
            {
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorNotOnDeclaringType, op.Symbol, TypeDisplay.Mist(type));
            }

            if(Complements.TryGetValue(op.Kind, out var complement)
                && !type.OperatorsList.Exists(other => other.Kind == complement && SameSignature(other, op)))
            {
                Report(
                    context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorUnpaired,
                    op.Symbol, TypeDisplay.Mist(type), Operator.SymbolOf(complement));
            }
        }
    }

    private static void CheckRefinements(AnalysisContext context, MistType type)
    {
        if(type is TypeAlias { AliasedType: { } aliased } alias)
        {
            foreach(var constituent in aliased is UnionType union ? union.Constituents : [aliased])
            {
                CheckPredicate(context, (constituent as RefinedType)?.Predicate, TypeDisplay.Mist(alias));
            }
        }

        if(type is IHasTypeParameters generic)
        {
            CheckTypeParameters(context, generic.TypeParametersList);
        }

        if(type is not MemberType member)
        {
            return;
        }

        foreach(var method in member.EnumerateDelegates())
        {
            CheckTypeParameters(context, method.TypeParametersList);
            CheckParameters(context, method.ParametersList);
        }

        foreach(var indexer in member.IndexersList)
        {
            CheckParameters(context, indexer.ParametersList);
        }
    }

    private static void CheckTypeParameters(AnalysisContext context, IEnumerable<TypeParameter> parameters)
    {
        foreach(var parameter in parameters)
        {
            CheckPredicate(context, parameter.Refinement, parameter.Name);
        }
    }

    private static void CheckParameters(AnalysisContext context, IEnumerable<Parameter> parameters)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.Type is RefinedType { IsAnonymous: true } refined)
            {
                CheckPredicate(context, refined.Predicate, parameter.Name);
            }
        }
    }

    private static void CheckPredicate(AnalysisContext context, Pattern? predicate, string constrained)
    {
        if(Comparability.Misordered(predicate) is { } comparison)
        {
            Report(
                context, comparison.Node, ProgramAnalysisDiagnostics.RefinementNotComparable,
                constrained, TypeDisplay.Mist(comparison.NarrowedType));
        }
    }

    private static bool Names(IMistType parameter, ConcreteType declaring)
    {
        return parameter switch
        {
            RefinedType refined => Names(refined.BaseType, declaring),
            LiftedType lifted => Names(lifted.Type, declaring),
            UnionType union => union.Constituents.Any(constituent => Names(constituent, declaring)),
            ConstructedType constructed => constructed.Definition.FullyQualifiedName == declaring.FullyQualifiedName,
            _ => parameter.FullyQualifiedName == declaring.FullyQualifiedName,
        };
    }

    private static bool SameSignature(Operator left, Operator right)
    {
        return left.ReturnType.FullyQualifiedName == right.ReturnType.FullyQualifiedName
            && left.ParametersList.Select(parameter => parameter.Type.FullyQualifiedName)
                .SequenceEqual(right.ParametersList.Select(parameter => parameter.Type.FullyQualifiedName));
    }

    private static void Report(AnalysisContext context, SyntaxNode? at, DiagnosticDescriptor descriptor, params object?[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }
}