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
    /// <summary>
    /// A hole is formatted by the runtime object's own ToString, never a Mist member: Mist has no
    /// override, so a type declared in Mist prints its name however it spells a ToString of its own.
    /// What prints differently for the same reference is a type whose ToString the program cannot
    /// see -- a CLR class, a container, or whatever an unknown, a type parameter or an abstract holds.
    /// </summary>
    internal static bool FormatsByType(IMistType type)
    {
        return type switch
        {
            LiftedType lifted => FormatsByType(lifted.Type),
            UnionType union => union.Constituents.All(FormatsByType),
            INamedTupleType named => FormatsByType(named.Underlying),
            ITupleType tuple => tuple.ElementTypes.All(FormatsByType),
            TypeAlias { AliasedType: { } aliased } => FormatsByType(aliased),
            RefinedType refined => FormatsByType(refined.BaseType),
            Newtype or EnumType or IFunctionType => true,
            _ => DefinitionOf(type) is ConcreteType concrete && (concrete.IsIntrinsic
                ? concrete.Modifiers.IsReadonly
                : concrete.DeclarationNode is not null && !ClrCallbacks.Implements(concrete, "System.IFormattable")),
        };
    }

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
            JudgeFromEnd(index);

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

        private void JudgeFromEnd(IndexExpression index)
        {
            if(index.FromEnd is { Count: var count } && !silenced.Contains(index))
            {
                JudgeGetter(count, count.Modifiers.IsStatic ? Origin.External : origins.Of(index.Receiver), index.Node, [index]);
            }
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

        private void JudgeTypeInitializer(Trigger trigger, Expression at)
        {
            if(trigger.Type is not (ConcreteType or EnumType) || silenced.Contains(at) || origins.InitializesStatics(trigger.Type))
            {
                return;
            }

            var initializer = TypeInitializer.Of(trigger.Type);

            if(initializer.NotRunBy(trigger, ScopeType())
                || !initialized.Add(initializer)
                || promiseOf(initializer) is not { Pure: false } promise)
            {
                return;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeCallsImpure, at.Node, [at],
                $"{Triggering(at)}, which runs the static initializers of '{trigger.Type.Name}',", scope, promise.Display, "static initializers that are pure");
        }

        private static string Triggering(Expression at)
        {
            return at switch
            {
                InvocationExpression { Method: { } method } => $"call '{method.Name}'",
                NewExpression allocation => $"call the constructor of '{TypeDisplay.Mist(allocation.Type)}'",
                DefaultExpression fallback => $"take the default of '{TypeDisplay.Mist(fallback.Type)}'",
                MemberAccessExpression access => $"read '{access.MemberName}'",
                FieldReferenceExpression field => $"read '{field.Field.Name}'",
                PropertyReferenceExpression property => $"read '{property.Property.Name}'",
                BinaryExpression { Operator: { } declared } => $"call operator {declared.Symbol}",
                UnaryExpression { Operator: { } declared } => $"call operator {declared.Symbol}",
                CastExpression { Operator: null } cast => $"convert to '{TypeDisplay.Mist(cast.TargetType)}'",
                CastExpression cast => $"call the conversion to '{TypeDisplay.Mist(cast.TargetType)}'",
                ImplicitConversionExpression conversion => $"call the conversion to '{TypeDisplay.Mist(conversion.TargetType)}'",
                _ => "use it",
            };
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

        private void JudgeCopy(Method conversion, Expression operand, Expression at)
        {
            if(conversion is CastOperator { CopiesClone: true } && !silenced.Contains(at) && !Origins.IsUnchangeable(operand.Type))
            {
                JudgeStorageRead(origins.Of(operand), $"a copy of {Spell(operand)}", at.Node, [at], operand);
            }
        }

        private void JudgeHoles(InterpolatedStringExpression interpolated)
        {
            foreach(var hole in interpolated.Parts.OfType<InterpolatedStringHole>())
            {
                JudgeFormat(hole.Expression);
            }
        }

        private void JudgeFormat(Expression formatted)
        {
            if(!silenced.Contains(formatted) && !FormatsByType(formatted.Type))
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeCallsImpure, formatted.Node, [formatted],
                    $"format {Spell(formatted)} through its runtime 'ToString'", scope, "unmarked",
                    "a value whose text its type fixes -- a primitive, a string, an enum or a type declared in Mist --");
            }
        }

        private static bool IsStable(Field field, Expression? receiver)
        {
            return field.Modifiers.IsReadonly
                || (field.DeclaringType is { } declaring && Declarations.IsReadonly(declaring))
                || receiver?.Type is AnonymousType;
        }

        private void JudgePattern(Pattern? pattern, Origin scrutinee, Expression about, IMistType? level)
        {
            switch(pattern)
            {
                case PropertyPattern property:
                    var holder = property.TypeTest is { } tested ? Unbox(tested, scrutinee, about, level) : scrutinee;
                    JudgePattern(property.TypeTest?.Refinement, holder, about, property.NarrowedType);

                    foreach(var subpattern in property.Subpatterns)
                    {
                        JudgePattern(subpattern.Pattern, JudgeSubpattern(subpattern, holder, about, property.NarrowedType), about, subpattern.MemberType);
                    }

                    break;

                case ListPattern { Reads: { } reads } list:
                    JudgeGetter(reads.Count, scrutinee, list.Node, [about]);

                    if(reads.Indexer.Getter is { } getter && !silenced.Contains(about))
                    {
                        _ = JudgeCall(getter, scrutinee, "read 'this[]'", list.Node, [about]);
                    }

                    JudgeElements(list, about);
                    break;

                case ListPattern list:
                    JudgeStorageRead(scrutinee, $"the elements of {Spell(about)}", list.Node, [about], chain: null);
                    JudgeElements(list, about);
                    break;


                case TuplePattern tuple:
                    foreach(var element in tuple.Elements)
                    {
                        JudgePattern(element, ThisTiers.IsValue(element.NarrowedType) ? scrutinee : Origin.External, about, level: null);
                    }

                    break;

                case ConjunctionPattern conjunction:
                    foreach(var operand in conjunction.Operands)
                    {
                        JudgePattern(operand, scrutinee, about, level);
                    }

                    break;

                case DisjunctionPattern disjunction:
                    foreach(var operand in disjunction.Operands)
                    {
                        JudgePattern(operand, scrutinee, about, level);
                    }

                    break;

                case NegatedPattern negated:
                    JudgePattern(negated.Operand, scrutinee, about, level);
                    break;

                case TypeTestPattern test:
                    JudgePattern(test.Refinement, Unbox(test, scrutinee, about, level), about, test.NarrowedType);
                    break;
            }
        }

        private void JudgeElements(ListPattern list, Expression about)
        {
            foreach(var element in list.Elements)
            {
                if(element.Subpattern is { } subpattern)
                {
                    JudgePattern(subpattern, ThisTiers.IsValue(subpattern.NarrowedType) ? Origin.Own : Origin.External, about, level: null);
                }
            }
        }

        private Origin Unbox(TypeTestPattern test, Origin scrutinee, Expression about, IMistType? level)
        {
            var type = test.NarrowedType;

            if(level is not null && Origins.Narrows(level, type))
            {
                return Origin.External;
            }

            if(level is null || ThisTiers.IsValue(level) || !ThisTiers.IsValue(type) || Origins.IsView(type))
            {
                return scrutinee;
            }

            if(DefinitionOf(type) is ConcreteType { IsIntrinsic: false } && !Declarations.IsReadonly(type) && !silenced.Contains(about))
            {
                JudgeStorageRead(scrutinee, $"the '{TypeDisplay.Mist(type)}' boxed in {Spell(about)}", test.Node, [about], about);
            }

            return Origin.Own;
        }

        private Origin JudgeSubpattern(PropertySubpattern subpattern, Origin scrutinee, Expression about, IMistType holderType)
        {
            if(subpattern.IsMemberFailed)
            {
                return Origin.Own;
            }

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