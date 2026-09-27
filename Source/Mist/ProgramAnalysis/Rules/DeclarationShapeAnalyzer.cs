using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
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
/// an alias, an enum base that is not readonly, two variants sharing a value, a default block naming
/// something that is not a field, an operator that is not public, is declared without its pair, is a
/// logical operator off Boolean, or takes no parameter of the type declaring it, a refinement predicate
/// -- every one <see cref="DeclaredPredicates"/> finds -- that orders a type <see cref="Comparability"/>
/// gives no order or reads a member <see cref="StableMembers"/> does not vouch for, a method,
/// constructor or indexer repeating an earlier one's signature on its type, and a member hiding one its
/// type inherits, which Mist admits only as an overload. Order 6 of the analysis register; each shape is classic's verdict, measured, or
/// the spec's where classic dropped the declaration unread, and the enum and sealed rules are the
/// full-accessibility-model and enum-readonly-restriction specs' own.
/// </summary>
internal sealed class DeclarationShapeAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types)
        {
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
                    CheckSignatures(context, concrete);
                    CheckRedeclarations(context, concrete);
                    break;

                case ConcreteType concrete:
                    CheckDefaults(context, concrete);
                    CheckOperators(context, concrete);
                    CheckSignatures(context, concrete);
                    break;

                case AbstractType abstractType:
                    CheckSignatures(context, abstractType);
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

        foreach(var declared in DeclaredPredicates.Of(context))
        {
            CheckPredicate(context, declared.Predicate, declared.Constrained);
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
            LiteralExpression { Value: string text } => text,
            LiteralExpression literal => IntegralConstant.Of(literal.Value),
            UnaryExpression { OperatorKind: UnaryOperatorKind.Negate, Operand: LiteralExpression literal } => -IntegralConstant.Of(literal.Value),
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

    private static void CheckPredicate(AnalysisContext context, Pattern? predicate, string constrained)
    {
        if(Comparability.Misordered(predicate) is { } comparison)
        {
            Report(
                context, comparison.Node, ProgramAnalysisDiagnostics.RefinementNotComparable,
                constrained, TypeDisplay.Mist(comparison.NarrowedType));
        }

        foreach(var unstable in UnstableMembers(predicate))
        {
            Report(context, unstable.Node, ProgramAnalysisDiagnostics.RefinementOverMutableMember, constrained, unstable.MemberName);
        }
    }

    private static IEnumerable<PropertySubpattern> UnstableMembers(Pattern? predicate)
    {
        var operands = predicate switch
        {
            ConjunctionPattern conjunction => conjunction.Operands,
            DisjunctionPattern disjunction => disjunction.Operands,
            NegatedPattern negated => [negated.Operand],
            _ => [],
        };

        foreach(var found in operands.SelectMany(UnstableMembers))
        {
            yield return found;
        }

        foreach(var subpattern in (predicate as PropertyPattern)?.Subpatterns ?? [])
        {
            if(!StableMembers.IsStable(subpattern.Member))
            {
                yield return subpattern;
            }

            foreach(var nested in UnstableMembers(subpattern.Pattern))
            {
                yield return nested;
            }
        }
    }

    private static void CheckSignatures(AnalysisContext context, MemberType type)
    {
        foreach(var methods in type.MethodsList.GroupBy(method => method.Name))
        {
            CheckRepeats(context, type, [.. methods], $"method '{methods.Key}'");
        }

        if(type is ConcreteType concrete)
        {
            CheckRepeats(context, type, concrete.ConstructorsList, "a constructor");
        }

        CheckRepeats(context, type, type.IndexersList, "an indexer");
    }

    private static void CheckRedeclarations(AnalysisContext context, ConcreteType type)
    {
        var interner = context.Model.TypeInterner;
        var path = new List<ConstructedType>();
        var seen = new HashSet<ConcreteType>();

        for(var baseType = type.BaseType; Inherits(baseType) is { } ancestor && seen.Add(ancestor); baseType = ancestor.BaseType)
        {
            if(baseType is ConstructedType constructed)
            {
                path.Add(constructed);
            }

            var inheritance = TypeSubstitution.ForPath(null, path, interner);

            foreach(var member in Members(type).Where(member => member.Node is not null))
            {
                if(!Members(ancestor).Any(other => other.Name == member.Name && Hides(member, other, inheritance, interner)))
                {
                    continue;
                }

                Report(
                    context, member.Node, ProgramAnalysisDiagnostics.MemberRedeclaration,
                    TypeDisplay.Mist(type), member.Name, TypeDisplay.Mist(ancestor));
            }
        }
    }

    private static bool Hides(Member member, Member inherited, TypeSubstitution inheritance, TypeInterner interner)
    {
        return member.Overload is not { } later || inherited.Overload is not { } earlier
            || OverloadSignature.Repeats(later, earlier, inheritance, interner);
    }

    private static IEnumerable<Member> Members(MemberType type)
    {
        foreach(var method in type.MethodsList)
        {
            yield return new Member(method.Name, method.DeclarationNode, method);
        }

        foreach(var indexer in type.IndexersList)
        {
            yield return new Member("this[]", indexer.DeclarationNode, indexer);
        }

        foreach(var property in type.PropertiesList)
        {
            yield return new Member(property.Name, property.DeclarationNode, null);
        }

        if(type is ConcreteType concrete)
        {
            foreach(var field in concrete.FieldsList)
            {
                yield return new Member(field.Name, field.DeclarationNode, null);
            }
        }
    }

    private static ConcreteType? Inherits(MistType? baseType)
    {
        return baseType switch
        {
            ConstructedType { Definition: ConcreteType constructed } => constructed,
            ConcreteType concrete => concrete,
            _ => null,
        };
    }

    private static void CheckRepeats<T>(AnalysisContext context, MemberType type, List<T> members, string description)
        where T : class, IOverload
    {
        for(var i = 1; i < members.Count; i++)
        {
            var later = members[i];

            if(!members.Take(i).Any(earlier => OverloadSignature.Repeats(later, earlier)))
            {
                continue;
            }

            Report(
                context, later.DeclarationNode, ProgramAnalysisDiagnostics.DuplicateSignature,
                TypeDisplay.Mist(type), description, OverloadFailure.DescribeSignatures([later]));
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

    private readonly record struct Member(string Name, SyntaxNode? Node, IOverload? Overload);

    private static void Report(AnalysisContext context, SyntaxNode? at, DiagnosticDescriptor descriptor, params object?[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }
}