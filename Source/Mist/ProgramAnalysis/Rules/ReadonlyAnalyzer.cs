using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// What a readonly context admits, walked structurally because the context is not a property of a node:
/// a readonly block turns one on part-way through a body, and which accessor a body belongs to decides
/// whether the backing field is exempt. Three rules hold everywhere -- a write through a readonly
/// parameter, a write to a readonly field outside a constructor, and a write inside a readonly member --
/// and the readonly-contexts spec's base rules (Req 11) hold inside a readonly one: no field written, no
/// setter called, no callee that is not readonly, no allocation through a constructor that is not.
/// <para>
/// Two privileges cut across them, both from the spec: a constructor may assign the fields of the
/// instance it builds and call its readonly setters, and a readonly setter may write its backing field. A readonly
/// BLOCK suspends the first for its extent (Req 11.13), which is the only reason the walk must be
/// structural rather than a sweep over nodes. A third comes from the purity spec (its Req 11): what the
/// context owns by <see cref="Origins"/> may be written, and a local member may be called on it. What
/// counts as readonly or local is <see cref="PurityInference"/>'s answer, declared or inferred, never a
/// second opinion. A member declared pure or local readonly is readonly too, but its body answers to
/// <see cref="PurityAnalyzer"/>, whose judge refuses everything these rules would and more, so it is
/// not a readonly context here: one mistake, one complaint.
/// </para>
/// </summary>
internal sealed partial class ReadonlyAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var inference = new PurityInference(context);
        var promises = new FormatPromises();

        foreach(var root in context.Bodies)
        {
            var frame = Frame.For(root);
            Walk(root.Root, frame, context, inference, promises);

            if(frame.IsReadonly)
            {
                JudgeExits(root.Root, root.Member, frame, context, inference);
            }
        }

        JudgeClosings(context, inference, promises);
    }

    private static void Walk(object node, Frame frame, AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        if(node is Block { IsReadonly: true } block)
        {
            var entered = frame.InsideReadonlyBlock();

            if(entered.IsReadonly && !frame.IsReadonly)
            {
                JudgeExits(block, member: null, entered, context, inference);
            }

            frame = entered;
        }

        Judge(node, frame, context, inference, promises);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            Walk(child, frame, context, inference, promises);
        }
    }

    private static void JudgeExits(object root, object? member, Frame frame, AnalysisContext context, PurityInference inference)
    {
        foreach(var exit in PurityJudge.ExitDrops(root, member))
        {
            if(!inference.IsReadonly(exit.Drop) && !inference.Of(exit.Drop).Local)
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, exit.At, [], exit.Drop.Name, frame.Describe());
            }
        }
    }

    private static void Judge(object node, Frame frame, AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        promises.Observe(node);
        JudgeReadonlyFieldEscapes(node, frame, context);
        JudgeCounted(node, frame, context, inference);

        if(frame.IsReadonly)
        {
            JudgeTriggers(node, frame, context, inference, promises);
        }

        switch(node)
        {
            case AssignmentExpression assignment:
                foreach(var (target, value) in Deconstructions.Pairs(assignment))
                {
                    JudgeAssignment(target, value, frame, context, inference);
                }

                break;

            case ObjectInitializerAssignment { IsTargetFailed: false } entry when frame.IsReadonly:
                JudgeEntry(entry, frame, context, inference);
                break;

            case InvocationExpression { Method: null } call when frame.IsReadonly:
                if(call.Receiver.Type is not IFunctionType { IsPure: true }
                    && !(call.Receiver is LocalReferenceExpression { Local: var holder } && frame.Origins.LambdaHeldBy(holder) is { } held && inference.Of(held).Readonly))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, call.Node, [call],
                        call.Receiver is LocalReferenceExpression named ? named.Local.Name : "the function value", frame.Describe());
                }

                break;

            case InvocationExpression invocation when frame.IsReadonly:
                var mutable = invocation.Method is { } callee && !inference.IsReadonly(callee) && !frame.CallsLocalOnOwn(invocation, callee, inference)
                    ? callee
                    : ElementComparisons.Of(invocation, out _)?.FirstOrDefault(compared => !inference.IsReadonly(compared));

                if(mutable is not null)
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, invocation.Node, [invocation],
                        mutable.Name, frame.Describe());
                }

                break;

            case ForEachStatement loop when frame.IsReadonly:
                JudgeLoop(loop, frame, context, inference);
                break;

            case InvocationExpression { Receiver: MemberAccessExpression { Receiver: var receiver } } call
                when call.Method is { } reached
                    && ReadonlyParameterReceivers.RootOf(receiver) is { Local.Parameter.Modifier.IsRef: false } through
                    && !inference.IsReadonly(reached):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyParameterCallsMutable, call.Node, [call],
                    reached.Name, ReadonlyParameterReceivers.Describe(receiver, through));
                break;

            case IndexExpression { Indexer: { } indexer } read when frame.IsReadonly && context.DirectionOf(node).HasFlag(AccessDirection.Read):
                if(!inference.IsReadonlyGetter(indexer) && !frame.LocalOnOwn(indexer.Getter, indexer.Modifiers, read, inference))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, read.Node, [read],
                        "this[]", frame.Describe());
                }

                break;

            case BinaryExpression { Operator: { Modifiers.IsIntrinsic: false } binaryOperator } binary
                when frame.IsReadonly && !inference.IsReadonly(binaryOperator):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, binary.Node, [binary],
                    $"operator {binaryOperator.Symbol}", frame.Describe());
                break;

            case BinaryExpression { Operator: null } comparison
                when frame.IsReadonly && ElementComparisons.OfAnonymous(comparison)?.FirstOrDefault(compared => !inference.IsReadonly(compared)) is { } comparing:
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, comparison.Node, [comparison], comparing.Name, frame.Describe());
                break;

            case UnaryExpression { Operator: { Modifiers.IsIntrinsic: false } unaryOperator } unary
                when frame.IsReadonly && !inference.IsReadonly(unaryOperator):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, unary.Node, [unary],
                    $"operator {unaryOperator.Symbol}", frame.Describe());
                break;

            case CastExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } cast
                when frame.IsReadonly && !inference.IsReadonly(conversion):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, cast.Node, [cast],
                    $"operator {TypeDisplay.Mist(conversion.TargetType)}", frame.Describe());
                break;

            case ImplicitConversionExpression { Operator: { Modifiers.IsIntrinsic: false } conversion } converted
                when frame.IsReadonly && !inference.IsReadonly(conversion):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, converted.Node, [converted],
                    $"operator {TypeDisplay.Mist(conversion.TargetType)}", frame.Describe());
                break;

            case CloneExpression clone when frame.IsReadonly:
                if(PurityJudge.CloneOf(clone.Type) is { } copied && !inference.IsReadonly(copied) && !frame.LocalOn(clone.Source, copied, inference))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, clone.Node, [clone],
                        copied.Name, frame.Describe());
                }

                break;

            case DefaultExpression fallback when frame.IsReadonly && PurityJudge.DefaultsTypeParameter(fallback):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextAllocatesMutable, fallback.Node, [fallback],
                    TypeDisplay.Mist(fallback.Type), frame.Describe());
                break;

            case NewExpression allocation when frame.IsReadonly:
                if(!AllocatesReadonly(allocation, inference))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextAllocatesMutable, allocation.Node, [allocation],
                        TypeDisplay.Mist(allocation.Type), frame.Describe());
                }

                break;

            case PropertyReferenceExpression read when frame.IsReadonly && context.DirectionOf(node).HasFlag(AccessDirection.Read):
                JudgeGetter(read.Property, read, frame, context, inference);
                break;

            case MemberAccessExpression { Member: PropertyResult { Property: { } accessed } } access
                when frame.IsReadonly && context.DirectionOf(node).HasFlag(AccessDirection.Read):
                JudgeGetter(accessed, access, frame, context, inference);
                break;

            case ExtensionPropertyAccessExpression extension when frame.IsReadonly && context.DirectionOf(node).HasFlag(AccessDirection.Read):
                JudgeGetter(extension.Property, extension, frame, context, inference);
                break;

            case PropertySubpattern { IsMemberFailed: false, Member: PropertyResult { Property: { } tested } } subpattern when frame.IsReadonly:
                if(!inference.IsReadonlyGetter(tested))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, subpattern.Node, [],
                        tested.Name, frame.Describe());
                }

                break;
        }
    }

    private static void JudgeTriggers(object node, Frame frame, AnalysisContext context, PurityInference inference, FormatPromises promises)
    {
        if(node is not Expression at)
        {
            return;
        }

        foreach(var trigger in TypeInitializer.TriggeredBy(node))
        {
            if(trigger.Type is ConcreteType or EnumType
                && TypeInitializer.Of(trigger.Type) is var initializer
                && !initializer.NotRunBy(trigger, frame.ScopeType())
                && !inference.Of(initializer).Readonly)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, at.Node, [at],
                    $"the static initializers of {trigger.Type.Name}", frame.Describe());
                return;
            }
        }

        JudgeFormatting(at, frame, context, inference, promises);
    }

    private static void JudgeCounted(object node, Frame frame, AnalysisContext context, PurityInference inference)
    {
        if(!frame.IsReadonly)
        {
            return;
        }

        if(node is IndexExpression { FromEnd.Count: { } count } fromEnd)
        {
            JudgeGetter(count, fromEnd, frame, context, inference);
        }

        if(node is ListPattern { Reads: { } reads } list)
        {
            foreach(var (name, readonlyGetter) in new[] { (reads.Count.Name, inference.IsReadonlyGetter(reads.Count)), ("this[]", inference.IsReadonlyGetter(reads.Indexer)) })
            {
                if(!readonlyGetter)
                {
                    context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, list.Node, [], name, frame.Describe());
                    break;
                }
            }
        }
    }

    private static bool AllocatesReadonly(NewExpression allocation, PurityInference inference)
    {
        if(allocation.Constructor is { } constructor)
        {
            return constructor.Modifiers.IsReadonly || inference.IsReadonly(constructor);
        }

        if(PurityJudge.AllocatesTypeParameter(allocation))
        {
            return false;
        }

        return PurityJudge.ImplicitConstructorOf(allocation) is not { } type || inference.Of(type).Readonly;
    }

    private static void JudgeReadonlyFieldEscapes(object node, Frame frame, AnalysisContext context)
    {
        var (arguments, parameters) = node switch
        {
            InvocationExpression invocation => (invocation.Arguments, invocation.Parameters),
            NewExpression allocation => (allocation.Arguments, allocation.Constructor?.ParametersList),
            _ => (null, null),
        };

        if(arguments is not null && parameters is not null)
        {
            for(var i = 0; i < arguments.Count && i < parameters.Count; i++)
            {
                if(parameters[i].Modifier is { IsRef: true, IsReadonly: false })
                {
                    JudgeReadonlyFieldEscape(arguments[i], frame, context);
                }
            }
        }

        if(node is LocalDeclarationStatement { Local: { IsRef: true, IsReadonly: false }, Initializer: { } referent })
        {
            JudgeReadonlyFieldEscape(referent, frame, context);
        }
    }

    private static void JudgeReadonlyFieldEscape(Expression target, Frame frame, AnalysisContext context)
    {
        if(Written(target) is { Modifiers.IsReadonly: true } field && !frame.Constructs(field, target))
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyFieldAssigned, target.Node, [target],
                field.Name, field.DeclaringType?.Name ?? "?");
        }
    }

    private static void JudgeEntry(ObjectInitializerAssignment entry, Frame frame, AnalysisContext context, PurityInference inference)
    {
        if(ReadonlyInference.StoresUnowned(frame.Origins, entry))
        {
            ReportStore(entry.Value, frame, context);
            return;
        }

        if(entry.Add is { } add)
        {
            if(!inference.IsReadonly(add) && !inference.Of(add).Local)
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, entry.Node, [entry.Value], add.Name, frame.Describe());
            }

            return;
        }

        if(entry.Member is PropertyResult { Property: { Setter: { } setter } property } && inference.Of(setter) is { Readonly: false, Local: false })
        {
            context.Report(ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty, entry.Node, [entry.Value], property.Name, frame.Describe());
        }
    }

    private static void JudgeGetter(
        Property property, Expression at, Frame frame, AnalysisContext context, PurityInference inference)
    {
        if(!inference.IsReadonlyGetter(property) && !frame.LocalOnOwn(property.Getter, property.Modifiers, at, inference))
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, at.Node, [at],
                property.Name, frame.Describe());
        }
    }

    private static void JudgeLoop(ForEachStatement loop, Frame frame, AnalysisContext context, PurityInference inference)
    {
        var collection = loop.Collection;

        if(ClrCallbacks.IsUnrecorded(loop))
        {
            context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, collection.Node, [collection], "GetEnumerator", frame.Describe());
            return;
        }

        foreach(var method in new[] { loop.GetEnumerator, loop.MoveNext, ClrCallbacks.DisposeOf(loop) })
        {
            if(method is not null && !inference.IsReadonly(method) && !frame.LocalOn(collection, method, inference))
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, collection.Node, [collection], method.Name, frame.Describe());
                return;
            }
        }

        if(loop.Current is { } current
            && !inference.IsReadonlyGetter(current)
            && !(frame.Origins.Of(collection) is Origin.Own && current.Getter is { } getter && inference.Of(getter).Local))
        {
            context.Report(ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, collection.Node, [collection], current.Name, frame.Describe());
        }
    }

    private static void ReportStore(Expression value, Frame frame, AnalysisContext context)
    {
        var origin = frame.Origins.Of(value) is Origin.Frozen ? "a frozen input" : "external";
        var named = value is LocalReferenceExpression local ? $"'{local.Local.Name}'" : "the value";

        context.Report(ProgramAnalysisDiagnostics.PureScopeStoresUnowned, value.Node, [value], named, $"readonly {frame.Describe()}", origin);
    }

    private static void JudgeAssignment(
        Expression target, Expression value, Frame frame, AnalysisContext context, PurityInference inference)
    {
        if(target is ThisExpression)
        {
            if(frame.IsReadonly && frame.Member is not Constructor)
            {
                context.Report(ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughRef, target.Node, [target], "this", frame.Describe());
            }

            return;
        }

        if(target is MemberAccessExpression { Receiver: var fieldReceiver }
            && Written(target) is { } through
            && ReadonlyParameterReceivers.RootOf(fieldReceiver) is { } parameter)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedOnReadonlyParameter, target.Node,
                [target], through.Name, ReadonlyParameterReceivers.Describe(fieldReceiver, parameter));
            return;
        }

        if(target is MemberAccessExpression { Receiver: var setReceiver }
            && SetProperty(target) is { } setOnParameter
            && ReadonlyParameterReceivers.RootOf(setReceiver) is { Local.Parameter.Modifier.IsRef: false } setThrough
            && !ReadonlyInference.Declared(setOnParameter.Setter, setOnParameter.Modifiers).IsReadonly)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyParameterSetsProperty, target.Node,
                [target], setOnParameter.Name, ReadonlyParameterReceivers.Describe(setReceiver, setThrough));
            return;
        }

        if(target is IndexExpression { Indexer: { } indexedOnParameter } setIndex
            && ReadonlyParameterReceivers.RootOf(setIndex.Receiver) is { Local.Parameter.Modifier.IsRef: false } indexThrough
            && !ReadonlyInference.Declared(indexedOnParameter.Setter, indexedOnParameter.Modifiers).IsReadonly)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyParameterSetsProperty, target.Node,
                [target], "this[]", ReadonlyParameterReceivers.Describe(setIndex.Receiver, indexThrough));
            return;
        }

        var own = frame.Owns(target);

        if(frame.IsReadonly && ReadonlyInference.StoresUnowned(frame.Origins, target, value))
        {
            ReportStore(value, frame, context);
            return;
        }

        if(target is InvocationExpression { Signature: { Modifiers.IsRef: true } referencing })
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughRef, target.Node, [target],
                    referencing.Name, frame.Describe());
            }

            return;
        }

        if(target is LocalReferenceExpression { Local: { IsRef: true } alias })
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughRef, target.Node, [target],
                    alias.Name, frame.Describe());
            }

            return;
        }

        if(ReadonlyInference.IndexedWriteTargetName(target) is { } reached)
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughIndex, target.Node,
                    [target], reached, frame.Describe());
            }

            return;
        }

        if(target is IndexExpression { Indexer: null } element)
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughIndex, target.Node,
                    [target], element.Receiver is LocalReferenceExpression handed ? handed.Local.Name : "the value", frame.Describe());
            }

            return;
        }

        if(target is IndexExpression { Indexer: { } indexer })
        {
            if(ReadonlyInference.Declared(indexer.Setter, indexer.Modifiers).IsReadonly)
            {
                return;
            }

            if(frame.IsReadonly && !(own && LocalSetter(indexer.Setter, inference)))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty, target.Node,
                    [target], "this[]", frame.Describe());
            }

            return;
        }

        if(SetProperty(target) is { } set)
        {
            if(frame.IsReadonly && !(own && LocalSetter(set.Setter, inference)) && !frame.MaySet(set, target, inference))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty, target.Node,
                    [target], set.Name, frame.Describe());
            }

            return;
        }

        if(target is BackingFieldReferenceExpression)
        {
            if(frame.IsReadonly && !frame.BackingFieldExempt)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, target.Node,
                    [target], "field", frame.MemberName());
            }

            return;
        }

        if(Written(target) is not { } field)
        {
            return;
        }

        if(frame.InReadonlyBlock && !own && (frame.IsReadonly || frame.SuspendedPrivilege(field)))
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyBlock, target.Node,
                [target], field.Name);
            return;
        }

        if(frame.Privileged && frame.Constructs(field, target))
        {
            return;
        }

        if(frame.IsReadonly && !own)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, target.Node,
                [target], field.Name, frame.MemberName());
        }
        else if(field.Modifiers.IsReadonly && !frame.Constructs(field, target) && !frame.PurityRefusesWrite(own))
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyFieldAssigned, target.Node,
                [target], field.Name, field.DeclaringType?.Name ?? "?");
        }
    }

    private static Property? SetProperty(Expression target)
    {
        return target switch
        {
            PropertyReferenceExpression property => property.Property,
            MemberAccessExpression { Member: PropertyResult found } => found.Property,
            ExtensionPropertyAccessExpression extension => extension.Property,
            _ => null,
        };
    }

    private static bool LocalSetter(PropertyAccessor? setter, PurityInference inference)
    {
        return setter is not null && inference.Of(setter).Local;
    }

    private static Field? Written(Expression target)
    {
        return target switch
        {
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Member: FieldResult found } => found.Field,
            _ => null,
        };
    }
}