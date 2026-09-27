using Mist.Common.Diagnostics;
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
/// Two privileges cut across them, both from the spec: a constructor may assign the fields of its own
/// type and call its own readonly setters, and a readonly setter may write its backing field. A readonly
/// BLOCK suspends the first for its extent (Req 11.13), which is the only reason the walk must be
/// structural rather than a sweep over nodes. A third comes from the purity spec (its Req 11): what the
/// context owns by <see cref="Origins"/> may be written, and a local member may be called on it. What
/// counts as readonly or local is <see cref="PurityInference"/>'s answer, declared or inferred, never a
/// second opinion. A member declared pure or local readonly is readonly too, but its body answers to
/// <see cref="PurityAnalyzer"/>, whose judge refuses everything these rules would and more, so it is
/// not a readonly context here: one mistake, one complaint.
/// </para>
/// </summary>
internal sealed class ReadonlyAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var inference = new PurityInference(context);

        foreach(var root in context.Bodies)
        {
            var frame = Frame.For(root);
            Walk(root.Root, frame, context, inference);

            if(frame.IsReadonly)
            {
                JudgeExits(root.Root, root.Member, frame, context, inference);
            }
        }
    }

    private static void Walk(object node, Frame frame, AnalysisContext context, PurityInference inference)
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

        Judge(node, frame, context, inference);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            Walk(child, frame, context, inference);
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

    private static void Judge(object node, Frame frame, AnalysisContext context, PurityInference inference)
    {
        JudgeReadonlyFieldEscapes(node, frame, context);

        switch(node)
        {
            case AssignmentExpression assignment:
                JudgeAssignment(assignment, frame, context, inference);
                break;

            case ObjectInitializerAssignment entry when frame.IsReadonly:
                JudgeEntry(entry, frame, context, inference);
                break;

            case InvocationExpression invocation when frame.IsReadonly:
                if(invocation.Method is { } callee && !inference.IsReadonly(callee) && !frame.CallsLocalOnOwn(invocation, callee, inference))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, invocation.Node, [invocation],
                        callee.Name, frame.Describe());
                }

                break;

            case ForEachStatement loop when frame.IsReadonly:
                JudgeLoop(loop, frame, context, inference);
                break;

            case InvocationExpression { Receiver: MemberAccessExpression { Receiver: LocalReferenceExpression { Local.Parameter.Modifier: { IsReadonly: true, IsRef: false } } through } } call
                when call.Method is { } reached && !inference.IsReadonly(reached):
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyParameterCallsMutable, call.Node, [call],
                    reached.Name, through.Local.Name);
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

            case PropertySubpattern { Member: PropertyResult { Property: { } tested } } subpattern when frame.IsReadonly:
                if(!inference.IsReadonlyGetter(tested))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, subpattern.Node, [],
                        tested.Name, frame.Describe());
                }

                break;
        }
    }

    private static bool AllocatesReadonly(NewExpression allocation, PurityInference inference)
    {
        if(allocation.Constructor is { } constructor)
        {
            return constructor.Modifiers.IsReadonly || inference.IsReadonly(constructor);
        }

        return PurityJudge.ImplicitConstructorOf(allocation) is not { } type || inference.Of(type).Readonly;
    }

    private static void JudgeReadonlyFieldEscapes(object node, Frame frame, AnalysisContext context)
    {
        var (arguments, parameters) = node switch
        {
            InvocationExpression invocation => (invocation.Arguments, invocation.Method?.ParametersList),
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

        foreach(var method in new[] { loop.GetEnumerator, loop.MoveNext })
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
        AssignmentExpression assignment, Frame frame, AnalysisContext context, PurityInference inference)
    {
        if(assignment.Target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local.Parameter.Modifier.IsReadonly: true } parameter }
            && Written(assignment.Target) is { } through)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedOnReadonlyParameter, assignment.Target.Node,
                [assignment.Target], through.Name, parameter.Local.Name);
            return;
        }

        if(assignment.Target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local.Parameter.Modifier: { IsReadonly: true, IsRef: false } } setThrough }
            && SetProperty(assignment.Target) is { } setOnParameter
            && !ReadonlyInference.Declared(setOnParameter.Setter, setOnParameter.Modifiers).IsReadonly)
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyParameterSetsProperty, assignment.Target.Node,
                [assignment.Target], setOnParameter.Name, setThrough.Local.Name);
            return;
        }

        var own = frame.Owns(assignment.Target);

        if(frame.IsReadonly && ReadonlyInference.StoresUnowned(frame.Origins, assignment))
        {
            ReportStore(assignment.Value, frame, context);
            return;
        }

        if(assignment.Target is LocalReferenceExpression { Local: { IsRef: true } alias })
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughRef, assignment.Target.Node, [assignment.Target],
                    alias.Name, frame.Describe());
            }

            return;
        }

        if(ReadonlyInference.IndexedWriteTargetName(assignment.Target) is { } reached)
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughIndex, assignment.Target.Node,
                    [assignment.Target], reached, frame.Describe());
            }

            return;
        }

        if(assignment.Target is IndexExpression { Indexer: null } element)
        {
            if(frame.IsReadonly && !own)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextWritesThroughIndex, assignment.Target.Node,
                    [assignment.Target], element.Receiver is LocalReferenceExpression handed ? handed.Local.Name : "the value", frame.Describe());
            }

            return;
        }

        if(assignment.Target is IndexExpression { Indexer: { } indexer } indexed)
        {
            if(ReadonlyInference.Declared(indexer.Setter, indexer.Modifiers).IsReadonly)
            {
                return;
            }

            if(indexed.Receiver is LocalReferenceExpression { Local.Parameter.Modifier: { IsReadonly: true, IsRef: false } } indexThrough)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyParameterSetsProperty, assignment.Target.Node,
                    [assignment.Target], "this[]", indexThrough.Local.Name);
            }
            else if(frame.IsReadonly && !(own && LocalSetter(indexer.Setter, inference)))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty, assignment.Target.Node,
                    [assignment.Target], "this[]", frame.Describe());
            }

            return;
        }

        if(SetProperty(assignment.Target) is { } set)
        {
            if(frame.IsReadonly && !(own && LocalSetter(set.Setter, inference)) && !frame.MaySet(set, inference))
            {
                context.Report(
                    ProgramAnalysisDiagnostics.ReadonlyContextSetsProperty, assignment.Target.Node,
                    [assignment.Target], set.Name, frame.Describe());
            }

            return;
        }

        if(assignment.Target is BackingFieldReferenceExpression)
        {
            if(frame.IsReadonly && !frame.BackingFieldExempt)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, assignment.Target.Node,
                    [assignment.Target], "field", frame.MemberName());
            }

            return;
        }

        if(Written(assignment.Target) is not { } field)
        {
            return;
        }

        if(frame.InReadonlyBlock && !own && (frame.IsReadonly || frame.SuspendedPrivilege(field)))
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyBlock, assignment.Target.Node,
                [assignment.Target], field.Name);
            return;
        }

        if(frame.MayAssign(field) && (!field.Modifiers.IsReadonly || frame.Constructs(field, assignment.Target)))
        {
            return;
        }

        if(frame.IsReadonly && !own)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, assignment.Target.Node,
                [assignment.Target], field.Name, frame.MemberName());
        }
        else if(field.Modifiers.IsReadonly && !frame.Constructs(field, assignment.Target) && !frame.PurityRefusesWrite(own))
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyFieldAssigned, assignment.Target.Node,
                [assignment.Target], field.Name, field.DeclaringType?.Name ?? "?");
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

    private sealed record Frame(
        object? Member, Origins Origins, MemberModifiers? Modifiers, bool IsReadonly, bool Privileged, bool BackingFieldExempt, bool InReadonlyBlock)
    {
        public static Frame For(SourceRoot root)
        {
            var origins = new Origins(root.Root, root.Member, ScopeKind.Readonly);
            var modifiers = ModifiersOf(root);

            return root.Member switch
            {
                Constructor constructor => new Frame(constructor, origins, modifiers, JudgedHere(modifiers), true, false, false),
                Method method => new Frame(method, origins, modifiers, JudgedHere(modifiers), false, false, false),
                Property property => new Frame(
                    property, origins, modifiers, JudgedHere(modifiers), false, IsSetterBody(property.Setter, root.Root), false),
                Indexer indexer => new Frame(indexer, origins, modifiers, JudgedHere(modifiers), false, false, false),
                _ => new Frame(root.Member, origins, modifiers, false, false, false, false),
            };
        }

        private static MemberModifiers? ModifiersOf(SourceRoot root)
        {
            return root.Member switch
            {
                Method method => method.Modifiers,
                Property property => ReadonlyInference.Declared(PurityInference.AccessorOf(property.Getter, property.Setter, root.Root), property.Modifiers),
                Indexer indexer => ReadonlyInference.Declared(PurityInference.AccessorOf(indexer.Getter, indexer.Setter, root.Root), indexer.Modifiers),
                _ => null,
            };
        }

        private static bool JudgedHere(MemberModifiers? modifiers)
        {
            return modifiers is { IsReadonly: true, IsPure: false, IsLocal: false };
        }

        public bool PurityRefusesWrite(bool own)
        {
            return Modifiers switch
            {
                { IsPure: true } => !own,
                { IsLocal: true, IsReadonly: true } => true,
                _ => false,
            };
        }

        public Frame InsideReadonlyBlock()
        {
            var judgedByPurity = Modifiers is { IsPure: true } or { IsLocal: true, IsReadonly: true };

            return this with { IsReadonly = IsReadonly || !judgedByPurity, Privileged = false, InReadonlyBlock = true };
        }

        public bool SuspendedPrivilege(Field field)
        {
            return Member is Constructor { DeclaringType: { } built } && ReferenceEquals(field.DeclaringType, built);
        }

        public bool Owns(Expression target)
        {
            return ReadonlyInference.HolderOf(Origins, target) is Origin.Own;
        }

        public bool CallsLocalOnOwn(InvocationExpression invocation, Method callee, PurityInference inference)
        {
            return ThisTiers.ReceiverOf(invocation) is { } receiver && LocalOn(receiver, callee, inference);
        }

        public bool LocalOn(Expression receiver, Method callee, PurityInference inference)
        {
            return !callee.Modifiers.IsStatic && Origins.Of(receiver) is Origin.Own && inference.Of(callee).Local;
        }

        public bool LocalOnOwn(PropertyAccessor? getter, MemberModifiers owner, Expression at, PurityInference inference)
        {
            return getter is not null
                && !owner.IsStatic
                && Owns(at)
                && inference.Of(getter).Local;
        }

        public bool MayAssign(Field field)
        {
            return Privileged
                && Member is Constructor { DeclaringType: { } built }
                && ReferenceEquals(field.DeclaringType, built);
        }

        public bool Constructs(Field field, Expression target)
        {
            if(Member is not Constructor { DeclaringType: { } built } constructor || !ReferenceEquals(field.DeclaringType, built))
            {
                return false;
            }

            if(field.Modifiers.IsStatic)
            {
                return constructor.Modifiers.IsStatic;
            }

            return !constructor.Modifiers.IsStatic && target is FieldReferenceExpression or MemberAccessExpression { Receiver: ThisExpression };
        }

        public bool MaySet(Property property, PurityInference inference)
        {
            return Privileged
                && Member is Constructor { DeclaringType: { } built }
                && ReferenceEquals(property.DeclaringType, built)
                && (property.Modifiers.IsReadonly || inference.IsReadonlyGetter(property));
        }

        public string MemberName()
        {
            return Member switch
            {
                Method method => method.Name,
                Property property => property.Name,
                Indexer => "this[]",
                _ => "?",
            };
        }

        public string Describe()
        {
            return Member switch
            {
                Constructor => "constructor",
                Method method => $"method '{method.Name}'",
                Property property => $"property '{property.Name}'",
                Indexer => "indexer",
                _ => "context",
            };
        }

        private static bool IsSetterBody(PropertyAccessor? setter, object root)
        {
            return setter is not null
                && (ReferenceEquals(setter.Body, root) || ReferenceEquals(setter.ExpressionBody, root));
        }
    }
}