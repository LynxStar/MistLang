using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The sites of <see cref="ContextCalls"/>: what each node of a body runs, and what each scope drops on
/// its way out, which has one home, <see cref="PurityJudge.ExitDrops"/>. A property's direction is the
/// assignment holding it as a target, read before its children are reached, so a getter is recorded for
/// a read, a setter for a write and both for a <c>??=</c>.
/// </summary>
internal sealed partial class ContextCalls
{
    private void Walk(SourceRoot root, Frame entered)
    {
        var pending = new Stack<(object Node, Frame Frame)>();
        var scopes = new List<(object Scope, Frame Frame)> { (root.Root, entered) };
        var declared = new Dictionary<LocalDeclarationStatement, Frame>(ReferenceEqualityComparer.Instance);
        var written = new HashSet<Expression>(ReferenceEqualityComparer.Instance);
        var readBack = new HashSet<Expression>(ReferenceEqualityComparer.Instance);
        pending.Push((root.Root, entered));

        while(pending.TryPop(out var visit))
        {
            var (node, frame) = visit;

            switch(node)
            {
                case WithBlockStatement scope:
                    if(!LifetimeOwners.IsView(scope.Value))
                    {
                        Drop(scope.Value, frame);
                    }

                    pending.Push((scope.Body, frame with { Held = new ContextHeld(scope.KeyType, frame.Held) }));
                    pending.Push((scope.Value, frame));
                    continue;

                case LocalFunctionStatement function:
                    _ = local.Add(function.Function);
                    var inner = frame with { Within = function.Function };
                    scopes.Add((function, inner));
                    pending.Push((function.Body, inner));
                    continue;

                case LambdaExpression lambda:
                    scopes.Add((lambda, frame));
                    break;

                case AsyncBlockExpression block:
                    scopes.Add((block.Body, frame));
                    break;

                case LocalDeclarationStatement declaration:
                    declared[declaration] = frame;
                    break;

                case AssignmentExpression assignment:
                    foreach(var (target, _) in Deconstructions.Pairs(assignment))
                    {
                        _ = written.Add(target);

                        if(assignment.IsCoalescing)
                        {
                            _ = readBack.Add(target);
                        }
                    }

                    break;

                case InvocationExpression { Method: { } callee } call:
                    Record(call.Node, call, callee, call.ContextTypes, frame);
                    break;

                case NewExpression { Constructor: { } constructor } allocation:
                    Record(allocation.Node, allocation, constructor, allocation.ContextTypes, frame);
                    break;

                case FunctionReferenceExpression reference:
                    Record(reference.Node, reference, reference.Method, reference.ContextTypes, frame);
                    break;

                case ForEachStatement loop:
                    foreach(var called in new object?[] { loop.GetEnumerator, loop.MoveNext, loop.Current?.Getter, ClrCallbacks.DisposeOf(loop) })
                    {
                        if(called is not null)
                        {
                            Record(loop.Collection.Node, loop.Collection, called, ContextBinding.TypesOf(called as Method), frame);
                        }
                    }

                    break;

                case ObjectInitializerAssignment { Add: { } add } element:
                    Record(element.Node, element.Value, add, ContextBinding.TypesOf(add), frame);
                    break;

                case ObjectInitializerAssignment { IsTargetFailed: false, Member: PropertyResult { Property.Setter: { } setter } } element:
                    Record(element.Node, element.Value, setter, [], frame);
                    break;

                case MemberAccessExpression { Member: PropertyResult { Property: var property } } access:
                    Reach(access, property.Getter, property.Setter, DirectionOf(access, written, readBack), frame);
                    break;

                case PropertyReferenceExpression reference:
                    Reach(reference, reference.Property.Getter, reference.Property.Setter, DirectionOf(reference, written, readBack), frame);
                    break;

                case ExtensionPropertyAccessExpression extension:
                    Reach(extension, extension.Property.Getter, extension.Property.Setter, DirectionOf(extension, written, readBack), frame);
                    break;

                case IndexExpression index:
                    if(index.Indexer is { } indexer)
                    {
                        Reach(index, indexer.Getter, indexer.Setter, DirectionOf(index, written, readBack), frame);
                    }

                    if(index.FromEnd is { Count.Getter: { } count })
                    {
                        Record(index.Node, index, count, [], frame);
                    }

                    break;

                case PropertySubpattern { IsMemberFailed: false, Member: PropertyResult { Property.Getter: { } getter } } subpattern:
                    Record(subpattern.Node, about: null, getter, [], frame);
                    break;

                case ListPattern { Reads: { } reads } list:
                    foreach(var read in new PropertyAccessor?[] { reads.Count.Getter, reads.Indexer.Getter })
                    {
                        if(read is not null)
                        {
                            Record(list.Node, about: null, read, [], frame);
                        }
                    }

                    break;

                case BinaryExpression { Operator: { } applied } binary:
                    Record(binary.Node, binary, applied, ContextBinding.TypesOf(applied, type => binary.OperatorClosing.Apply(type, interner: null)), frame);
                    break;

                case UnaryExpression { Operator: { } applied } unary:
                    Record(unary.Node, unary, applied, ContextBinding.TypesOf(applied, type => unary.OperatorClosing.Apply(type, interner: null)), frame);
                    break;

                case CastExpression { Operator: { } called } cast:
                    Record(cast.Node, cast, called, ContextBinding.TypesOf(called), frame);
                    break;

                case ImplicitConversionExpression { Operator: { } called } conversion:
                    Record(conversion.Node, conversion, called, ContextBinding.TypesOf(called), frame);
                    break;
            }

            foreach(var child in BodyWalker.ChildrenOf(node).Reverse())
            {
                pending.Push((child, frame));
            }
        }

        foreach(var (scope, frame) in scopes)
        {
            foreach(var exit in PurityJudge.ExitDrops(scope, root.Member))
            {
                Record(exit.At, about: null, exit.Drop, ContextBinding.TypesOf(exit.Drop), exit.Declaration is { } declaration ? declared.GetValueOrDefault(declaration, frame) : frame);
            }
        }
    }

    private void Drop(Expression value, Frame frame)
    {
        foreach(var (drop, _) in PurityJudge.Drops(value.Type, awaitable: false))
        {
            Record(value.Node, value, drop, ContextBinding.TypesOf(drop), frame);
        }
    }

    private void Reach(Expression access, PropertyAccessor? getter, PropertyAccessor? setter, AccessDirection direction, Frame frame)
    {
        if(getter is not null && direction.HasFlag(AccessDirection.Read))
        {
            Record(access.Node, access, getter, [], frame);
        }

        if(setter is not null && direction.HasFlag(AccessDirection.Write))
        {
            Record(access.Node, access, setter, [], frame);
        }
    }

    private static AccessDirection DirectionOf(Expression access, HashSet<Expression> written, HashSet<Expression> readBack)
    {
        if(!written.Contains(access))
        {
            return AccessDirection.Read;
        }

        return readBack.Contains(access) ? AccessDirection.ReadWrite : AccessDirection.Write;
    }

    private static IEnumerable<(Constructor From, Constructor Chained, IReadOnlyList<IMistType> Keys)> Chains(ConcreteType type)
    {
        var throughBase = TypeSubstitution.ForReceiver(type.BaseType as ConstructedType);

        foreach(var constructor in type.ConstructorsList)
        {
            if(!constructor.Modifiers.IsStatic && PurityJudge.ChainedOf(constructor) is Constructor chained)
            {
                var closing = constructor.ChainKind == ConstructorChainKind.This ? TypeSubstitution.Empty : throughBase;

                yield return (constructor, chained, ContextBinding.TypesOf(chained, key => closing.Apply(key, interner: null)));
            }
        }
    }

    public static IEnumerable<(AbstractType Abstract, Method Slot, ConformanceAnswers.MethodAnswer Answer, SyntaxNode? At)> Answers(
        ConcreteType concrete, TypeInterner interner)
    {
        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();

        foreach(var (abstractType, substitution, mappings, at) in Reaches(concrete, interner))
        {
            foreach(var slot in abstractType.MethodsList.Where(method => !method.Modifiers.IsStatic))
            {
                if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner) is { } answer)
                {
                    yield return (abstractType, slot, answer, at);
                }
            }
        }
    }

    private static IEnumerable<(AbstractType Abstract, PropertyAccessor Slot, PropertyAccessor Answering)> AccessorAnswers(
        ConcreteType concrete, TypeInterner interner)
    {
        var fillers = ConformanceAnalyzer.Fillers(concrete).ToList();

        foreach(var (abstractType, substitution, mappings, _) in Reaches(concrete, interner))
        {
            foreach(var slot in abstractType.PropertiesList.Where(property => !property.Modifiers.IsStatic))
            {
                if(ConformanceAnswers.Property(slot, substitution, fillers, mappings, interner)?.Member is { } property)
                {
                    foreach(var (answered, answering) in Paired(slot.Getter, slot.Setter, property.Getter, property.Setter))
                    {
                        yield return (abstractType, answered, answering);
                    }
                }
            }

            foreach(var slot in abstractType.IndexersList.Where(indexer => !indexer.Modifiers.IsStatic))
            {
                if(ConformanceAnswers.Indexer(slot, substitution, fillers, interner) is { } indexer)
                {
                    foreach(var (answered, answering) in Paired(slot.Getter, slot.Setter, indexer.Getter, indexer.Setter))
                    {
                        yield return (abstractType, answered, answering);
                    }
                }
            }
        }
    }

    private static IEnumerable<(PropertyAccessor Slot, PropertyAccessor Answering)> Paired(
        PropertyAccessor? slotGetter, PropertyAccessor? slotSetter, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        if(slotGetter is not null && getter is not null)
        {
            yield return (slotGetter, getter);
        }

        if(slotSetter is not null && setter is not null)
        {
            yield return (slotSetter, setter);
        }
    }

    private static IEnumerable<(AbstractType Abstract, TypeSubstitution Substitution, ConformanceDeclaration? Mappings, SyntaxNode? At)> Reaches(
        ConcreteType concrete, TypeInterner interner)
    {
        var reaches = concrete.BaseAbstractTypes.Select(named => (Named: (IMistType)named, Mappings: (ConformanceDeclaration?)null))
            .Concat(concrete.ConformancesList.Select(conformance => (Named: conformance.AbstractType, Mappings: (ConformanceDeclaration?)conformance)));

        foreach(var (named, mappings) in reaches)
        {
            foreach(var (abstractType, substitution) in ConformanceAnalyzer.Reached([named], interner, concrete))
            {
                yield return (abstractType, substitution, mappings, mappings?.DeclarationNode ?? concrete.DeclarationNode);
            }
        }
    }
}