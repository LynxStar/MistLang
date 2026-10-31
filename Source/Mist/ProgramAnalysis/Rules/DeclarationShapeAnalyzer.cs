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
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Refusals decidable from a declaration alone: a base or a newtype source that is sealed, `sealed` on
/// an alias, an enum base that is not readonly, two variants sharing a value, a default block naming
/// something that is not a field, an operator that is not public, is declared without its pair, is a
/// logical operator off Boolean, or takes no parameter of the type declaring it, a refinement predicate
/// -- every one <see cref="DeclaredPredicates"/> finds -- that orders a type <see cref="Comparability"/>
/// gives no order, compares a constant no value of the type could equal, so that nothing could ever
/// discharge it, or reads a member <see cref="StableMembers"/> does not vouch for or that has no getter, a method,
/// constructor or indexer repeating an earlier one's signature on its type, a member hiding one its
/// type inherits, which Mist admits only as an overload, and a constructor writing no chain to a base
/// <see cref="ImplicitChains"/> finds nothing to run in, and a dependent parameter named like a member its
/// <see cref="TypeParameter.ValueProperty"/> would collide with. Each shape is classic's verdict, measured, or
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
            if(type is MemberType member and not ConcreteType { CloneSource: not null })
            {
                foreach(var parameter in DependentNameConflicts(member))
                {
                    Report(context, member.DeclarationNode, ProgramAnalysisDiagnostics.DependentParameterConflictsWithMember, new NameValue(parameter.Name), DiagnosticSnapshot.Type(member));
                }
            }

            switch(type)
            {
                case ConcreteType { CloneSource: { } source, NewtypeDeclaration: { } newtypeDeclaration }:
                    if(Declarations.IsSealed(source))
                    {
                        Report(context, newtypeDeclaration, ProgramAnalysisDiagnostics.NewtypeOverSealedType, DiagnosticSnapshot.Type(source));
                    }

                    break;

                case ConcreteType { BaseType: { } baseType } concrete:
                    if(Declarations.IsSealed(baseType))
                    {
                        Report(context, concrete.DeclarationNode, ProgramAnalysisDiagnostics.SealedBaseType, DiagnosticSnapshot.Type(baseType));
                    }

                    CheckDefaults(context, concrete);
                    CheckOperators(context, concrete);
                    CheckSignatures(context, concrete);
                    CheckRedeclarations(context, concrete);
                    CheckImplicitChains(context, concrete);
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
                    Report(context, newtype.DeclarationNode, ProgramAnalysisDiagnostics.NewtypeOverSealedType, DiagnosticSnapshot.Type(source));
                    break;

                case TypeAlias { Modifiers.IsSealed: true } alias:
                    Report(context, alias.DeclarationNode, ProgramAnalysisDiagnostics.SealedTypeAlias);
                    break;

                case EnumType enumType:
                    CheckEnum(context, enumType);
                    break;
            }
        }

        foreach(var repeat in ExtensionSignatureDecision.Repeats(context.Extensions))
        {
            var related = new RelatedLocation(RelatedRole.PreviousDeclaration, DiagnosticHelper.ResolveLocation(repeat.Earlier).Location);
            if(repeat.IsProperty)
            {
                context.ReportRelated(ProgramAnalysisDiagnostics.DuplicateExtensionProperty, repeat.Later, [], [related],
                    new NameValue(repeat.Name), DiagnosticSnapshot.Type(repeat.Receiver));
            }
            else
            {
                context.ReportRelated(ProgramAnalysisDiagnostics.DuplicateExtensionMethod, repeat.Later, [], [related],
                    new NameValue(repeat.Name), DiagnosticSnapshot.Type(repeat.Receiver), new NameValue(repeat.Signature));
            }
        }

        foreach(var backing in context.Nodes.OfType<BackingFieldReferenceExpression>().Where(backing => ComputedPropertyDecision.RefusesField(backing.Property)))
        {
            Report(context, backing.Node, ProgramAnalysisDiagnostics.FieldInComputedProperty, new NameValue(backing.Property.Name));
        }

        var purity = new PurityInference(context);

        foreach(var declared in DeclaredPredicates.Of(context))
        {
            CheckPredicate(context, declared.Predicate, declared.Constrained, purity);
        }
    }

    public static IEnumerable<TypeParameter> DependentNameConflicts(MemberType type)
    {
        var members = (type as ConcreteType)?.FieldsList.Select(field => field.Name) ?? [];
        members = members
            .Concat(type.PropertiesList.Select(property => property.Name))
            .Concat(type.MethodsList.Select(method => method.Name))
            .Concat(type.NestedTypes.Select(nested => MistType.StripArity(nested.Name)));
        var named = members.ToHashSet(StringComparer.Ordinal);

        foreach(var parameter in type.TypeParametersList)
        {
            if(parameter is { IsDependent: true, IsPack: false } && named.Contains(parameter.Name))
            {
                yield return parameter;
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
                Report(context, enumType.DeclarationNode, ProgramAnalysisDiagnostics.EnumBaseNotReadonly, DiagnosticSnapshot.Type(enumType, enumType.Name), DiagnosticSnapshot.Type(constituent));
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
                    ProgramAnalysisDiagnostics.EnumDuplicateValue, new NameValue(variant.Name), new NameValue(first), DiagnosticSnapshot.Type(enumType, enumType.Name));
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

                Report(context, at, ProgramAnalysisDiagnostics.DefaultEntryNotAField, new NameValue(entry.FieldName), DiagnosticSnapshot.Type(type, type.Name));
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
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorNotPublic, new NameValue(op.Symbol));
            }

            if(op.Kind is OperatorKind.LogicalAnd or OperatorKind.LogicalOr
                && type.FullyQualifiedName != WellKnownTypes.Boolean.FullyQualifiedName)
            {
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.LogicalOperatorDeclared, new NameValue(op.Symbol));
                continue;
            }

            if(!op.ParametersList.Exists(parameter => Names(parameter.Type, type)))
            {
                Report(context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorNotOnDeclaringType, new NameValue(op.Symbol), DiagnosticSnapshot.Type(type));
            }

            if(Complements.TryGetValue(op.Kind, out var complement)
                && !type.OperatorsList.Exists(other => other.Kind == complement && SameSignature(other, op)))
            {
                Report(
                    context, op.DeclarationNode, ProgramAnalysisDiagnostics.OperatorUnpaired,
                    new NameValue(op.Symbol), DiagnosticSnapshot.Type(type), new NameValue(Operator.SymbolOf(complement)));
            }
        }
    }

    private static void CheckPredicate(AnalysisContext context, Pattern? predicate, string constrained, PurityInference purity)
    {
        if(Comparability.Misordered(predicate) is { } comparison)
        {
            Report(
                context, comparison.Node, ProgramAnalysisDiagnostics.RefinementNotComparable,
                new NameValue(constrained), DiagnosticSnapshot.Type(comparison.NarrowedType));
        }

        if(Comparability.Mismatched(predicate, context.Model.TypeInterner) is { } mismatched)
        {
            Report(
                context, mismatched.Node, ProgramAnalysisDiagnostics.RefinementConstantMismatch,
                new NameValue(constrained), DiagnosticSnapshot.Type(mismatched.Bound.Type), DiagnosticSnapshot.Type(mismatched.NarrowedType));
        }

        foreach(var (read, refusal) in RefusedReads(predicate, purity))
        {
            if(refusal == ProgramAnalysisDiagnostics.NoGetAccessor)
            {
                Report(context, read.Node, refusal, new NameValue(read.MemberName));
            }
            else
            {
                Report(context, read.Node, refusal, new NameValue(constrained), new NameValue(read.MemberName));
            }
        }
    }

    public static MistDiagnostic? PredicateRead(LookupResult member, PurityInference? purity)
    {
        if(member is PropertyResult { Property.Getter: null })
        {
            return ProgramAnalysisDiagnostics.NoGetAccessor;
        }

        return StableMembers.IsStable(member, purity) ? null : ProgramAnalysisDiagnostics.RefinementOverMutableMember;
    }

    private static IEnumerable<(PropertySubpattern Read, MistDiagnostic Refusal)> RefusedReads(Pattern? predicate, PurityInference purity)
    {
        var operands = predicate switch
        {
            ConjunctionPattern conjunction => conjunction.Operands,
            DisjunctionPattern disjunction => disjunction.Operands,
            NegatedPattern negated => [negated.Operand],
            _ => [],
        };

        foreach(var found in operands.SelectMany(operand => RefusedReads(operand, purity)))
        {
            yield return found;
        }

        foreach(var subpattern in (predicate as PropertyPattern)?.Subpatterns ?? [])
        {
            if(!subpattern.IsMemberFailed && PredicateRead(subpattern.Member, purity) is { } refusal)
            {
                yield return (subpattern, refusal);
            }

            foreach(var nested in RefusedReads(subpattern.Pattern, purity))
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
            CheckCallableSignatures(context, concrete);
        }

        CheckRepeats(context, type, type.IndexersList, "an indexer");
    }

    private static void CheckCallableSignatures(AnalysisContext context, ConcreteType type)
    {
        foreach(var repeat in CallableSignatureDecision.Operators(type.OperatorsList)
            .Concat(CallableSignatureDecision.Casts(type.CastOperatorsList)))
        {
            var related = new RelatedLocation(RelatedRole.PreviousDeclaration, DiagnosticHelper.ResolveLocation(repeat.Earlier.DeclarationNode!).Location);
            if(repeat.Later is CastOperator cast)
            {
                context.ReportRelated(CallableSignatureDiagnostics.DuplicateCastSignature, cast.DeclarationNode!, [], [related],
                    DiagnosticSnapshot.Type(type), new EnumValue(cast.IsImplicit ? "implicit" : "explicit"),
                    DiagnosticSnapshot.Type(cast.SourceParameter.Type), DiagnosticSnapshot.Type(cast.TargetType));
            }
            else if(repeat.Later is Operator op)
            {
                context.ReportRelated(CallableSignatureDiagnostics.DuplicateOperatorSignature, op.DeclarationNode!, [], [related],
                    DiagnosticSnapshot.Type(type), new NameValue(op.Symbol),
                    new SequenceValue(SlotKind.Type, [.. op.ParametersList.Select(parameter => DiagnosticSnapshot.Type(parameter.Type))]));
            }
        }
    }

    private static void CheckRedeclarations(AnalysisContext context, ConcreteType type)
    {
        var interner = context.Model.TypeInterner;
        var path = new List<ConstructedType>();
        var seen = new HashSet<ConcreteType>();
        var slotted = NativeSlotFills.Of(type, interner).Select(fill => fill.Filler.Member)
            .Concat(NativeSlotFills.Collisions(type, interner).Where(collision => collision.Kind != NativeSlotKind.None).Select(collision => collision.Member.Member))
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var reportedAgainstProjected = new HashSet<object>(ReferenceEqualityComparer.Instance);

        for(var baseType = type.BaseType; Inherits(baseType) is { } ancestor && seen.Add(ancestor); baseType = ancestor.BaseType)
        {
            if(baseType is ConstructedType constructed)
            {
                path.Add(constructed);
            }

            var inheritance = TypeSubstitution.ForPath(null, path, interner);
            var projected = ancestor.Assembly.Kind == MistAssemblyKind.Ambient;

            foreach(var member in Members(type).Where(member => member.Node is not null && !slotted.Contains(member.Source)))
            {
                if(!Members(ancestor).Any(other => other.Name == member.Name && Hides(member, other, inheritance, interner, projected))
                    || (projected && !reportedAgainstProjected.Add(member.Source)))
                {
                    continue;
                }

                Report(
                    context, member.Node, ProgramAnalysisDiagnostics.MemberRedeclaration,
                    DiagnosticSnapshot.Type(type), new NameValue(member.Name), DiagnosticSnapshot.Type(ancestor));
            }
        }
    }

    private static void CheckImplicitChains(AnalysisContext context, ConcreteType type)
    {
        foreach(var constructor in type.ConstructorsList)
        {
            if(ImplicitChains.UnsatisfiedStructBase(constructor) is { } structBase)
            {
                Report(context, constructor.DeclarationNode, ProgramAnalysisDiagnostics.StructBaseConstructorRequired,
                    DiagnosticSnapshot.Type(structBase), DiagnosticSnapshot.Type(type));
            }

            if(ImplicitChains.UnsatisfiedBase(constructor) is { } baseType)
            {
                Report(context, constructor.DeclarationNode, ProgramAnalysisDiagnostics.BaseConstructorUnsatisfied, DiagnosticSnapshot.Type(baseType));
            }
        }
    }

    private static bool Hides(Member member, Member inherited, TypeSubstitution inheritance, TypeInterner interner, bool projected)
    {
        return member.Overload is not { } later || inherited.Overload is not { } earlier
            || (projected
                ? OverloadSignature.RepeatsProjected(later, earlier, inheritance, interner)
                : OverloadSignature.Repeats(later, earlier, inheritance, interner));
    }

    private static IEnumerable<Member> Members(MemberType type)
    {
        foreach(var method in type.MethodsList)
        {
            yield return new Member(method.Name, method.DeclarationNode, method, method);
        }

        foreach(var indexer in type.IndexersList)
        {
            yield return new Member("this[]", indexer.DeclarationNode, indexer, indexer);
        }

        foreach(var property in type.PropertiesList)
        {
            yield return new Member(property.Name, property.DeclarationNode, null, property);
        }

        if(type is ConcreteType concrete)
        {
            foreach(var field in concrete.FieldsList)
            {
                yield return new Member(field.Name, field.DeclarationNode, null, field);
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
                DiagnosticSnapshot.Type(type), new NameValue(description), new SequenceValue(SlotKind.Type, [.. later.ParametersList.Select(parameter => DiagnosticSnapshot.Type(parameter.Type))], ItemPrefix: "", ItemSuffix: ""));
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

    private readonly record struct Member(string Name, SyntaxNode? Node, IOverload? Overload, object Source);

    private static void Report(AnalysisContext context, SyntaxNode? at, MistDiagnostic descriptor, params SlotValue[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }
}