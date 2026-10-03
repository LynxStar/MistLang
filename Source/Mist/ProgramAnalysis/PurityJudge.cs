using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Rules;
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
            ConcreteType type => new($"{Spell(kind)} constructor of '{MistType.StripArity(type.Name)}'"),
            LocalFunctionStatement function => LocalFunction(kind, function.Function.Name),
            Method method => new($"{Spell(kind)} method '{method.Name}'"),
            Property property => new($"{Spell(kind)} property '{property.Name}'"),
            Indexer => new($"{Spell(kind)} indexer"),
            LambdaExpression => Lambda(kind),
            Field field => FieldInitializer(field.Name, field.DeclaringType?.Name ?? "?"),
            TypeInitializer initializer => new($"the static initializers of '{initializer.Type.Name}'"),
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
/// table refuses. Nested lambdas and local functions are scopes of their own and are not walked; calling
/// one is judged as a call to the member whose promise <c>promiseOf</c> answers, with its captures as the
/// receiver. A lambda root judged with a member's Origins rather than a closure's is an initializer
/// written as a bare lambda: the scope's VALUE, whose creation is admitted (Requirement 7.4) and whose
/// body is judged as a closure where it converts. A struct-typed storage read is judged once, at the
/// outermost hop, because a field of a by-value copy is the copy's and only the copy's source could be
/// external; and a refused operation silences the reads along its receiver chain, which fail for the
/// same reason it did. A local scope may keep an argument it was handed (Requirement 5.2), but only in
/// an unowned slot of its own instance: an owned slot, or any slot of something else the scope owns,
/// would make the scope's next read through it answer Own for an object other references share.
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
        private readonly HashSet<TypeInitializer> initialized = new(ReferenceEqualityComparer.Instance);

        public List<Finding> Findings { get; } = [];

        public void Run(object root)
        {
            var start = root switch
            {
                LambdaExpression lambda when origins.IsClosure => (object?)lambda.Body ?? lambda.Result,
                LambdaExpression => null,
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
            foreach(var (target, _) in Deconstructions.Pairs(assignment))
            {
                _ = written.Add(target);

                if(assignment.IsCoalescing)
                {
                    _ = readBack.Add(target);
                }

                for(var hop = ReceiverOf(target); hop is not null && IsValueStorage(hop); hop = ReceiverOf(hop))
                {
                    _ = written.Add(hop);
                }
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

                case CloneExpression clone:
                    JudgeClone(clone);
                    break;

                case InvocationExpression invocation:
                    JudgeInvocation(invocation);
                    break;

                case NewExpression allocation:
                    JudgeAllocation(allocation);
                    break;

                case DefaultExpression fallback:
                    JudgeDefault(fallback);
                    break;

                case BinaryExpression { Operator: { } declared } binary:
                    _ = JudgeCall(declared, Origin.External, $"call operator {declared.Symbol}", binary.Node, [binary]);
                    break;

                case BinaryExpression { Operator: null, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Concatenate } concatenation
                    when DefinitionOf(concatenation.Type) is ConcreteType { IsIntrinsic: true, Name: "String" }:
                    JudgeFormat(concatenation.Left);
                    JudgeFormat(concatenation.Right);
                    break;

                case BinaryExpression { Operator: null, OperatorKind: BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual } comparison
                    when comparison.Left.Type is AnonymousType || comparison.Right.Type is AnonymousType:
                    JudgeComparisons(ElementComparisons.OfAnonymous(comparison), "compare anonymous values", comparison);
                    break;

                case UnaryExpression { Operator: { } declared } unary:
                    _ = JudgeCall(declared, Origin.External, $"call operator {declared.Symbol}", unary.Node, [unary]);
                    break;

                case CastExpression { Operator: { } declared } cast:
                    _ = JudgeCall(declared, Origin.External, $"call the conversion to '{TypeDisplay.Mist(declared.TargetType)}'", cast.Node, [cast]);
                    JudgeCopy(declared, cast.Operand, cast);
                    break;

                case ImplicitConversionExpression { Operator: { } declared } conversion:
                    _ = JudgeCall(declared, Origin.External, $"call the conversion to '{TypeDisplay.Mist(declared.TargetType)}'", conversion.Node, [conversion]);
                    JudgeCopy(declared, conversion.Operand, conversion);
                    break;

                case ForEachStatement loop:
                    JudgeLoop(loop);
                    break;

                case InterpolatedStringExpression interpolated:
                    JudgeHoles(interpolated);
                    break;

                case IsPatternExpression test:
                    JudgePattern(test.Pattern, origins.Of(test.Scrutinee), test.Scrutinee, test.Scrutinee.Type);
                    break;

                case SwitchExpression chosen:
                    foreach(var arm in chosen.Arms)
                    {
                        JudgePattern(arm.Pattern, origins.Of(chosen.Scrutinee), chosen.Scrutinee, chosen.Scrutinee.Type);
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

            if(node is Expression triggering)
            {
                foreach(var trigger in TypeInitializer.TriggeredBy(node))
                {
                    JudgeTypeInitializer(trigger, triggering);
                }
            }
        }

        private void JudgeAssignment(AssignmentExpression assignment)
        {
            foreach(var (target, value) in Deconstructions.Pairs(assignment))
            {
                JudgeAssignment(target, value);
            }
        }

        private void JudgeAssignment(Expression target, Expression value)
        {
            switch(target)
            {
                case LocalReferenceExpression local:
                    if(JudgeLocalWrite(local) && origins.ReferentOf(local.Local) is { } referent)
                    {
                        JudgeStore(value, new Slot(origins.Of(local), origins.IsOwnedSlot(referent), OfThis: false));
                    }

                    break;

                case FieldReferenceExpression field:
                    if(JudgeWrite(HolderOf(field.Field, receiver: null), $"field '{field.Field.Name}'", target))
                    {
                        JudgeStore(value, SlotOf(field.Field, receiver: null));
                    }

                    break;

                case MemberAccessExpression { Member: FieldResult found } access:
                    if(JudgeWrite(HolderOf(found.Field, access.Receiver), $"field '{found.Field.Name}'", target))
                    {
                        JudgeStore(value, SlotOf(found.Field, access.Receiver));
                    }

                    break;

                case BackingFieldReferenceExpression backing:
                    if(JudgeWrite(HolderOf(backing.Property, receiver: null), "'field'", target))
                    {
                        JudgeStore(value, SlotOf(backing.Property, receiver: null));
                    }

                    break;

                case PropertyReferenceExpression property:
                    JudgeSet(property.Property.Setter, property.Property.Name, SlotOf(property.Property, receiver: null), target, value);
                    break;

                case MemberAccessExpression { Member: PropertyResult found } access:
                    JudgeSet(found.Property.Setter, found.Property.Name, SlotOf(found.Property, access.Receiver), target, value);
                    break;

                case ExtensionPropertyAccessExpression extension:
                    JudgeSet(extension.Property.Setter, extension.Property.Name, SlotOf(extension.Property, extension.Receiver), target, value);
                    break;

                case IndexExpression { Indexer: null } element:
                    if(JudgeWrite(origins.Of(element.Receiver), $"an element of {Spell(element.Receiver)}", target))
                    {
                        JudgeStore(value, new Slot(origins.Of(element.Receiver), Owned: false, OfThis: false));
                    }

                    break;

                case IndexExpression { Indexer: { } indexer } indexed:
                    JudgeFromEnd(indexed);
                    JudgeSet(indexer.Setter, "this[]", new Slot(origins.Of(indexed.Receiver), Owned: false, indexed.Receiver is ThisExpression), target, value);
                    break;

                case ThisExpression:
                    _ = JudgeWrite(origins.This, "'this'", target);
                    break;

                case InvocationExpression { Signature: { Modifiers.IsRef: true } method }:
                    if(JudgeWrite(origins.OfReferent(target), $"through the reference '{method.Name}' returns", target))
                    {
                        JudgeStore(value, new Slot(origins.OfReferent(target), Owned: true, OfThis: false));
                    }

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

        private bool JudgeLocalWrite(LocalReferenceExpression reference)
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

                return false;
            }

            return local.IsRef && JudgeWrite(origins.Of(reference), $"'{local.Name}'", reference);
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

            JudgeStore(entry.Value, new Slot(Origin.Own, owned, OfThis: false), initializing: true);
        }

        private void JudgeStore(Expression value, Slot slot, bool initializing = false)
        {
            if((kind is ScopeKind.LocalReadonly && !initializing) || Origins.IsUnchangeable(value.Type))
            {
                return;
            }

            var origin = origins.Of(value);

            if(slot.Owned && origin is Origin.Own && OwnershipAnalyzer.HeldByAnotherOwner(value, origins.Member as Method) is { } field)
            {
                Refuse(
                    ProgramAnalysisDiagnostics.PureScopeStoresUnowned, value.Node, [value], Spell(value), scope,
                    $"held by the owned field '{field}', which keeps it,");
                return;
            }

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
                return origins.InitializesStatics(field.DeclaringType) ? Origin.Own : Origin.External;
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