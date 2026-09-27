using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal static partial class PurityJudge
{
    private sealed partial class Walk
    {
        private void JudgeFieldRead(Expression at, Field field, Expression? receiver)
        {
            if(IsStable(field, receiver) || (receiver is not null && IsValueStorage(receiver)))
            {
                return;
            }

            JudgeStorageRead(HolderOf(field, receiver), $"field '{field.Name}'", at.Node, [at], receiver);
        }

        private void JudgeBackingFieldRead(BackingFieldReferenceExpression backing)
        {
            var property = backing.Property;

            if(property.Setter is null or { IsInit: true })
            {
                return;
            }

            JudgeStorageRead(HolderOf(property, receiver: null), "'field'", backing.Node, [backing], chain: null);
        }

        private void JudgeIndexRead(IndexExpression index)
        {
            if(index.Indexer is { } indexer)
            {
                if(indexer.Getter is { } getter)
                {
                    _ = JudgeCall(getter, origins.Of(index.Receiver), "read 'this[]'", index.Node, [index]);
                }

                return;
            }

            if(IsValueStorage(index.Receiver))
            {
                return;
            }

            JudgeStorageRead(origins.Of(index.Receiver), $"an element of {Spell(index.Receiver)}", index.Node, [index], index.Receiver);
        }

        private void JudgeLocalRead(LocalReferenceExpression reference)
        {
            var local = reference.Local;

            if(!origins.IsOwnBinding(local))
            {
                if(kind is not ScopeKind.Pure || origins.ReadableCapture(local))
                {
                    return;
                }

                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeReadsMutable, reference.Node, [reference],
                    $"captured variable '{local.Name}'", scope, "it can be assigned after the capture, so its value is not fixed for the lambda's life");
                return;
            }

            if(local.IsRef)
            {
                JudgeStorageRead(origins.Of(reference), $"'{local.Name}'", reference.Node, [reference], reference);
            }
        }

        private void JudgeGetter(Property property, Origin holder, SyntaxNode at, IReadOnlyList<Expression> about)
        {
            if(property.Getter is { } getter)
            {
                _ = JudgeCall(getter, holder, $"read '{property.Name}'", at, about);
            }
        }

        private void JudgeStorageRead(Origin origin, string what, SyntaxNode at, IReadOnlyList<Expression> about, Expression? chain)
        {
            if(origin is Origin.Own || (origin is Origin.Frozen && kind is ScopeKind.Pure))
            {
                return;
            }

            if(kind is not ScopeKind.Pure && RootArgument(chain) is { } argument)
            {
                Refuse(ProgramAnalysisDiagnostics.LocalScopeReadsArgument, at, about, what, scope, argument.Name);
                return;
            }

            var reason = (kind, origin) switch
            {
                (ScopeKind.Pure, _) => "it is external and can change between reads; a pure scope reads only what it owns, what it holds frozen, and stable fields",
                (_, Origin.Frozen) => "it is a frozen input, which lies outside the scope's own instance",
                _ => "it lies outside the scope's own instance; a local scope reads only its own instance and stable fields",
            };

            Refuse(ProgramAnalysisDiagnostics.PureScopeReadsMutable, at, about, what, scope, reason);
        }

        private LocalVariable? RootArgument(Expression? chain)
        {
            for(var current = chain; current is not null; current = ReceiverOf(current) ?? Operand(current))
            {
                if(current is LocalReferenceExpression { Local: { Parameter: not null } local } && origins.IsOwnBinding(local))
                {
                    return local;
                }
            }

            return null;
        }

        private static Expression? Operand(Expression expression)
        {
            return expression switch
            {
                ImplicitConversionExpression { Operator: null } conversion => conversion.Operand,
                CastExpression { Operator: null } cast => cast.Operand,
                AsExpression converted => converted.Operand,
                _ => null,
            };
        }

        private static bool IsStable(Field field, Expression? receiver)
        {
            return field.Modifiers.IsReadonly
                || (field.DeclaringType is { } declaring && Declarations.IsReadonly(declaring))
                || receiver?.Type is AnonymousType;
        }

        private void JudgePattern(Pattern? pattern, Origin scrutinee, Expression about)
        {
            switch(pattern)
            {
                case PropertyPattern property:
                    JudgePattern(property.TypeTest, scrutinee, about);

                    foreach(var subpattern in property.Subpatterns)
                    {
                        JudgePattern(subpattern.Pattern, JudgeSubpattern(subpattern, scrutinee, about, property.NarrowedType), about);
                    }

                    break;

                case ListPattern list:
                    JudgeStorageRead(scrutinee, $"the elements of {Spell(about)}", list.Node, [about], chain: null);

                    foreach(var element in list.Elements)
                    {
                        if(element.Subpattern is { } subpattern)
                        {
                            JudgePattern(subpattern, ThisTiers.IsValue(subpattern.NarrowedType) ? Origin.Own : Origin.External, about);
                        }
                    }

                    break;

                case TuplePattern tuple:
                    foreach(var element in tuple.Elements)
                    {
                        JudgePattern(element, ThisTiers.IsValue(element.NarrowedType) ? scrutinee : Origin.External, about);
                    }

                    break;

                case ConjunctionPattern conjunction:
                    foreach(var operand in conjunction.Operands)
                    {
                        JudgePattern(operand, scrutinee, about);
                    }

                    break;

                case DisjunctionPattern disjunction:
                    foreach(var operand in disjunction.Operands)
                    {
                        JudgePattern(operand, scrutinee, about);
                    }

                    break;

                case NegatedPattern negated:
                    JudgePattern(negated.Operand, scrutinee, about);
                    break;

                case TypeTestPattern { Refinement: { } refinement }:
                    JudgePattern(refinement, scrutinee, about);
                    break;
            }
        }

        private Origin JudgeSubpattern(PropertySubpattern subpattern, Origin scrutinee, Expression about, IMistType holderType)
        {
            switch(subpattern.Member)
            {
                case FieldResult { Field: var field }:
                    if(!IsStable(field, receiver: null))
                    {
                        JudgeStorageRead(field.Modifiers.IsStatic ? Origin.External : scrutinee, $"field '{field.Name}'", subpattern.Node, [about], chain: null);
                    }

                    return Origins.Through(scrutinee, field, holderType);

                case PropertyResult { Property: var property }:
                    JudgeGetter(property, property.Modifiers.IsStatic ? Origin.External : scrutinee, subpattern.Node, [about]);
                    return ThisTiers.IsValue(property.Type) ? Origin.Own : Origin.External;

                default:
                    return Origin.Own;
            }
        }
    }
}