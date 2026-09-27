using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal sealed record Finding(DiagnosticDescriptor Descriptor, SyntaxNode At, IReadOnlyList<Expression> About, object?[] Args);

internal readonly record struct ScopeDescription(string Text)
{
    public static ScopeDescription Of(ScopeKind kind, object? member)
    {
        return member switch
        {
            Constructor constructor => new($"{Spell(kind)} constructor of '{constructor.DeclaringType?.Name ?? constructor.Name}'"),
            LocalFunctionStatement function => LocalFunction(kind, function.Function.Name),
            Method method => new($"{Spell(kind)} method '{method.Name}'"),
            Property property => new($"{Spell(kind)} property '{property.Name}'"),
            Indexer => new($"{Spell(kind)} indexer"),
            LambdaExpression => Lambda(kind),
            Field field => FieldInitializer(field.Name, field.DeclaringType?.Name ?? "?"),
            _ => new($"the {Spell(kind)} scope"),
        };
    }

    public static ScopeDescription Lambda(ScopeKind kind)
    {
        return new($"the {Spell(kind)} lambda");
    }

    public static ScopeDescription LocalFunction(ScopeKind kind, string name)
    {
        return new($"{Spell(kind)} function '{name}'");
    }

    public static ScopeDescription FieldInitializer(string field, string type)
    {
        return new($"the initializer of field '{field}' of '{type}'");
    }

    public static ScopeDescription DefaultEntry(string field, string type)
    {
        return new($"the default entry for field '{field}' of '{type}'");
    }

    public static string Spell(ScopeKind kind)
    {
        return kind switch
        {
            ScopeKind.Pure => "pure",
            ScopeKind.Local => "local",
            _ => "local readonly",
        };
    }

    public override string ToString()
    {
        return Text;
    }
}

/// <summary>
/// One walk over a body under a scope kind, yielding a finding for each operation the purity design's
/// table refuses: writes outside what the scope owns, reads of what can change, calls the scope may not
/// make, stores of unowned references into owned instances, and the constructs that reach outside the
/// program. Nested lambdas and local functions are scopes of their own and are not walked; calling one
/// is judged as a call to the member whose promise <c>promiseOf</c> answers, with its captures as the
/// receiver. A struct-typed storage read is judged once, at the outermost hop, because a field of a
/// by-value copy is the copy's and only the copy's source could be external; and a refused operation
/// silences the reads along its receiver chain, which fail for the same reason it did.
/// </summary>
internal static partial class PurityJudge
{
    public static IEnumerable<Finding> Judge(
        object root, ScopeKind kind, ScopeDescription scope, Origins origins, Func<object, Promise> promiseOf)
    {
        var walk = new Walk(kind, scope, origins, promiseOf);
        walk.Run(root);
        return walk.Findings;
    }

    private sealed partial class Walk(ScopeKind kind, ScopeDescription scope, Origins origins, Func<object, Promise> promiseOf)
    {
        private readonly HashSet<object> written = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> readBack = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> silenced = new(ReferenceEqualityComparer.Instance);

        public List<Finding> Findings { get; } = [];

        public void Run(object root)
        {
            var start = root switch
            {
                LambdaExpression lambda => (object?)lambda.Body ?? lambda.Result,
                LocalFunctionStatement function => function.Body,
                _ => root,
            };

            if(start is null)
            {
                return;
            }

            var nodes = ScopeNodes(start).ToList();

            foreach(var assignment in nodes.OfType<AssignmentExpression>())
            {
                MarkWritten(assignment);
            }

            foreach(var node in nodes)
            {
                JudgeNode(node);
            }

            foreach(var exit in ExitDrops(root, origins.Member, nodes))
            {
                _ = JudgeCall(exit.Drop, Origin.Own, $"{(exit.IsAwait ? "await" : "drop")} '{exit.Name}' at scope exit", exit.At, []);
            }
        }

        private void MarkWritten(AssignmentExpression assignment)
        {
            _ = written.Add(assignment.Target);

            if(assignment.IsCoalescing)
            {
                _ = readBack.Add(assignment.Target);
            }

            for(var hop = ReceiverOf(assignment.Target); hop is not null && IsValueStorage(hop); hop = ReceiverOf(hop))
            {
                _ = written.Add(hop);
            }
        }

        private bool IsRead(object node)
        {
            return !silenced.Contains(node) && (!written.Contains(node) || readBack.Contains(node));
        }

        private void JudgeNode(object node)
        {
            switch(node)
            {
                case AssignmentExpression assignment:
                    JudgeAssignment(assignment);
                    break;

                case ObjectInitializerAssignment entry:
                    JudgeInitializerEntry(entry);
                    break;

                case FieldReferenceExpression field when IsRead(field):
                    JudgeFieldRead(field, field.Field, receiver: null);
                    break;

                case MemberAccessExpression { Member: FieldResult found } access when IsRead(access):
                    JudgeFieldRead(access, found.Field, access.Receiver);
                    break;

                case MemberAccessExpression { Member: PropertyResult found } access when IsRead(access):
                    JudgeGetter(found.Property, HolderOf(found.Property, access.Receiver), access.Node, [access]);
                    break;

                case PropertyReferenceExpression property when IsRead(property):
                    JudgeGetter(property.Property, HolderOf(property.Property, receiver: null), property.Node, [property]);
                    break;

                case ExtensionPropertyAccessExpression extension when IsRead(extension):
                    JudgeGetter(extension.Property, origins.Of(extension.Receiver), extension.Node, [extension]);
                    break;

                case BackingFieldReferenceExpression backing when IsRead(backing):
                    JudgeBackingFieldRead(backing);
                    break;

                case IndexExpression index when IsRead(index):
                    JudgeIndexRead(index);
                    break;

                case LocalReferenceExpression local when IsRead(local):
                    JudgeLocalRead(local);
                    break;

                case CloneExpression { Source: LocalReferenceExpression or ThisExpression } clone:
                    JudgeStorageRead(origins.Of(clone.Source), $"a clone of {Spell(clone.Source)}", clone.Node, [clone], clone.Source);
                    break;

                case InvocationExpression invocation:
                    JudgeInvocation(invocation);
                    break;

                case NewExpression allocation:
                    JudgeAllocation(allocation);
                    break;

                case BinaryExpression { Operator: { } declared } binary:
                    _ = JudgeCall(declared, Origin.External, $"call operator {declared.Symbol}", binary.Node, [binary]);
                    break;

                case UnaryExpression { Operator: { } declared } unary:
                    _ = JudgeCall(declared, Origin.External, $"call operator {declared.Symbol}", unary.Node, [unary]);
                    break;

                case CastExpression { Operator: { } declared } cast:
                    _ = JudgeCall(declared, Origin.External, $"call the conversion to '{TypeDisplay.Mist(declared.TargetType)}'", cast.Node, [cast]);
                    break;

                case ImplicitConversionExpression { Operator: { } declared } conversion:
                    _ = JudgeCall(declared, Origin.External, $"call the conversion to '{TypeDisplay.Mist(declared.TargetType)}'", conversion.Node, [conversion]);
                    break;

                case ForEachStatement loop:
                    JudgeLoop(loop);
                    break;

                case IsPatternExpression test:
                    JudgePattern(test.Pattern, origins.Of(test.Scrutinee), test.Scrutinee);
                    break;

                case SwitchExpression chosen:
                    foreach(var arm in chosen.Arms)
                    {
                        JudgePattern(arm.Pattern, origins.Of(chosen.Scrutinee), chosen.Scrutinee);
                    }

                    break;

                case WithBlockStatement ambient:
                    Refuse(
                        ProgramAnalysisDiagnostics.PureScopeCallsImpure, ambient.Value.Node, [ambient.Value],
                        "open a with block", scope, "an ambient binding, which reaches state outside the scope", "a body without one");
                    break;

                case AsyncBlockExpression block:
                    Refuse(
                        ProgramAnalysisDiagnostics.PureScopeCallsImpure, block.Node, [block],
                        "run an async block", scope, "a task that runs outside the scope", "a synchronous body");
                    break;
            }
        }

        private void JudgeAssignment(AssignmentExpression assignment)
        {
            var target = assignment.Target;

            switch(target)
            {
                case LocalReferenceExpression local:
                    JudgeLocalWrite(local);
                    break;

                case FieldReferenceExpression field:
                    if(JudgeWrite(HolderOf(field.Field, receiver: null), $"field '{field.Field.Name}'", target))
                    {
                        JudgeStore(assignment.Value, SlotOf(field.Field, receiver: null));
                    }

                    break;

                case MemberAccessExpression { Member: FieldResult found } access:
                    if(JudgeWrite(HolderOf(found.Field, access.Receiver), $"field '{found.Field.Name}'", target))
                    {
                        JudgeStore(assignment.Value, SlotOf(found.Field, access.Receiver));
                    }

                    break;

                case BackingFieldReferenceExpression backing:
                    if(JudgeWrite(HolderOf(backing.Property, receiver: null), "'field'", target))
                    {
                        JudgeStore(assignment.Value, SlotOf(backing.Property, receiver: null));
                    }

                    break;

                case PropertyReferenceExpression property:
                    JudgeSet(property.Property.Setter, property.Property.Name, SlotOf(property.Property, receiver: null), target, assignment.Value);
                    break;

                case MemberAccessExpression { Member: PropertyResult found } access:
                    JudgeSet(found.Property.Setter, found.Property.Name, SlotOf(found.Property, access.Receiver), target, assignment.Value);
                    break;

                case ExtensionPropertyAccessExpression extension:
                    JudgeSet(extension.Property.Setter, extension.Property.Name, SlotOf(extension.Property, extension.Receiver), target, assignment.Value);
                    break;

                case IndexExpression { Indexer: null } element:
                    if(JudgeWrite(origins.Of(element.Receiver), $"an element of {Spell(element.Receiver)}", target))
                    {
                        JudgeStore(assignment.Value, new Slot(origins.Of(element.Receiver), Owned: false, OfThis: false));
                    }

                    break;

                case IndexExpression { Indexer: { } indexer } indexed:
                    JudgeSet(indexer.Setter, "this[]", new Slot(origins.Of(indexed.Receiver), Owned: false, indexed.Receiver is ThisExpression), target, assignment.Value);
                    break;
            }
        }

        private Slot SlotOf(Field field, Expression? receiver)
        {
            return new Slot(HolderOf(field, receiver), ThisTiers.IsOwned(field), receiver is null or ThisExpression);
        }

        private Slot SlotOf(Property property, Expression? receiver)
        {
            return new Slot(HolderOf(property, receiver), property.Modifiers.IsOwned && !property.Modifiers.IsStatic, receiver is null or ThisExpression);
        }

        private void JudgeLocalWrite(LocalReferenceExpression reference)
        {
            var local = reference.Local;

            if(!origins.IsOwnBinding(local))
            {
                if(kind is ScopeKind.Pure)
                {
                    Refuse(
                        ProgramAnalysisDiagnostics.PureScopeWrites, reference.Node, [reference],
                        $"captured variable '{local.Name}'", scope, "it is a variable of the enclosing scope, and a pure lambda writes nothing outside itself");
                }
                else if(kind is ScopeKind.LocalReadonly)
                {
                    _ = JudgeWrite(Origin.Own, $"captured variable '{local.Name}'", reference);
                }

                return;
            }

            if(local.IsRef)
            {
                _ = JudgeWrite(origins.Of(reference), $"'{local.Name}'", reference);
            }
        }

        private bool JudgeWrite(Origin holder, string what, Expression at)
        {
            if(kind is ScopeKind.LocalReadonly)
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeWrites, at.Node, [at],
                    what, scope, "a local readonly scope writes nothing, its own instance included");
                return false;
            }

            if(holder is Origin.Own)
            {
                return true;
            }

            Refuse(
                ProgramAnalysisDiagnostics.PureScopeWrites, at.Node, [at],
                what, scope, $"it is {Describe(holder)}, and a {ScopeDescription.Spell(kind)} scope writes only what it owns");
            return false;
        }

        private void JudgeSet(PropertyAccessor? setter, string name, Slot slot, Expression target, Expression value)
        {
            if(!JudgeWrite(slot.Holder, $"'{name}'", target))
            {
                return;
            }

            if(setter is not null)
            {
                _ = JudgeCall(setter, slot.Holder, $"set '{name}'", target.Node, [target]);
            }

            JudgeStore(value, slot);
        }

        private void JudgeInitializerEntry(ObjectInitializerAssignment entry)
        {
            if(entry.Add is { } add)
            {
                var promise = JudgeCall(add, Origin.Own, $"call '{add.Name}'", entry.Node, [entry.Value]);
                JudgeArguments(promise, new Slot(Origin.Own, Owned: false, OfThis: false), [entry.Value]);
                return;
            }

            var owned = entry.Member switch
            {
                PropertyResult { Property: var property } => property.Modifiers.IsOwned,
                FieldResult { Field: var field } => ThisTiers.IsOwned(field),
                _ => false,
            };

            if(entry.Member is PropertyResult { Property.Setter: { } setter } found)
            {
                _ = JudgeCall(setter, Origin.Own, $"set '{found.Property.Name}'", entry.Node, [entry.Value]);
            }

            JudgeStore(entry.Value, new Slot(Origin.Own, owned, OfThis: false));
        }

        /// <summary>
        /// Requirement 6.4 and the design's store row. A local scope may keep an argument it was handed
        /// (Requirement 5.2), but only in an unowned slot of its own instance: an owned slot, or any slot
        /// of something else the scope owns, would make the scope's next read through it answer Own for
        /// an object other references share.
        /// </summary>
        private void JudgeStore(Expression value, Slot slot)
        {
            if(kind is ScopeKind.LocalReadonly || Origins.IsUnchangeable(value.Type))
            {
                return;
            }

            var origin = origins.Of(value);

            if(origin is Origin.Own || (origin is Origin.External && kind is ScopeKind.Local && slot is { Owned: false, OfThis: true } && IsInput(value)))
            {
                return;
            }

            Refuse(ProgramAnalysisDiagnostics.PureScopeStoresUnowned, value.Node, [value], Spell(value), scope, Describe(origin));
        }

        private bool IsInput(Expression value)
        {
            return Unwrapped(value) is LocalReferenceExpression { Local: var argument } && origins.IsInput(argument);
        }

        private Origin HolderOf(Field field, Expression? receiver)
        {
            if(field.Modifiers.IsStatic)
            {
                return Origin.External;
            }

            return receiver is null ? origins.This : origins.Of(receiver);
        }

        private Origin HolderOf(Property property, Expression? receiver)
        {
            if(property.Modifiers.IsStatic)
            {
                return Origin.External;
            }

            return receiver is null ? origins.This : origins.Of(receiver);
        }

        private void Refuse(DiagnosticDescriptor descriptor, SyntaxNode at, IReadOnlyList<Expression> about, params object?[] args)
        {
            Findings.Add(new Finding(descriptor, at, about, args));

            foreach(var expression in about)
            {
                for(var hop = (Expression?)expression; hop is not null; hop = ChainOf(hop))
                {
                    _ = silenced.Add(hop);
                }
            }
        }

        private static Expression? ChainOf(Expression expression)
        {
            return expression switch
            {
                InvocationExpression call => ThisTiers.ReceiverOf(call),
                _ => ReceiverOf(expression) ?? Operand(expression),
            };
        }

        private static string Describe(Origin origin)
        {
            return origin is Origin.Frozen ? "a frozen input" : "external";
        }

        private static Expression? ReceiverOf(Expression expression)
        {
            return expression switch
            {
                MemberAccessExpression access => access.Receiver,
                IndexExpression index => index.Receiver,
                ExtensionPropertyAccessExpression extension => extension.Receiver,
                _ => null,
            };
        }

        private static bool IsValueStorage(Expression expression)
        {
            return expression is FieldReferenceExpression or MemberAccessExpression { Member: FieldResult }
                or BackingFieldReferenceExpression or IndexExpression { Indexer: null }
                && ThisTiers.IsValue(expression.Type);
        }

        private static Expression Unwrapped(Expression expression)
        {
            return expression switch
            {
                ImplicitConversionExpression { Operator: null } conversion => Unwrapped(conversion.Operand),
                CastExpression { Operator: null } cast => Unwrapped(cast.Operand),
                _ => expression,
            };
        }

        private static string Spell(Expression expression)
        {
            return Path(expression) is { } path ? $"'{path}'" : "the value";
        }

        private readonly record struct Slot(Origin Holder, bool Owned, bool OfThis);

        private static string? Path(Expression expression)
        {
            return expression switch
            {
                LocalReferenceExpression local => local.Local.Name,
                ThisExpression => "this",
                FieldReferenceExpression field => field.Field.Name,
                PropertyReferenceExpression property => property.Property.Name,
                BackingFieldReferenceExpression => "field",
                TypeReferenceExpression type => type.ReferencedType.Name,
                MemberAccessExpression access => Path(access.Receiver) is { } receiver ? $"{receiver}.{access.MemberName}" : null,
                ExtensionPropertyAccessExpression extension => Path(extension.Receiver) is { } receiver ? $"{receiver}.{extension.Property.Name}" : null,
                IndexExpression index => Path(index.Receiver) is { } receiver ? $"{receiver}[]" : null,
                ImplicitConversionExpression { Operator: null } conversion => Path(conversion.Operand),
                _ => null,
            };
        }
    }
}