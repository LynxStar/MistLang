using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
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
/// structural rather than a sweep over nodes. What counts as readonly is
/// <see cref="ReadonlyInference"/>'s answer, declared or inferred, never a second opinion.
/// </para>
/// </summary>
internal sealed class ReadonlyAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var inference = new ReadonlyInference();

        foreach(var root in SourceBodies.Of(context.Model))
        {
            Walk(root.Root, Frame.For(root), new HashSet<object>(ReferenceEqualityComparer.Instance), context, inference);
        }
    }

    private static void Walk(
        object node, Frame frame, HashSet<object> targets, AnalysisContext context, ReadonlyInference inference)
    {
        if(node is Block { IsReadonly: true })
        {
            frame = frame.InsideReadonlyBlock();
        }

        Judge(node, frame, targets, context, inference);

        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            Walk(child, frame, targets, context, inference);
        }
    }

    private static void Judge(
        object node, Frame frame, HashSet<object> targets, AnalysisContext context, ReadonlyInference inference)
    {
        switch(node)
        {
            case AssignmentExpression { IsCoalescing: false } assignment:
                _ = targets.Add(assignment.Target);
                JudgeAssignment(assignment, frame, context, inference);
                break;

            case InvocationExpression invocation when frame.IsReadonly:
                if(invocation.Method is { } callee && !inference.IsReadonly(callee))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, invocation.Node, [invocation],
                        callee.Name, frame.Describe());
                }

                break;

            case NewExpression allocation when frame.IsReadonly:
                if(allocation.Constructor is { Modifiers.IsReadonly: false } constructor && !inference.IsReadonly(constructor))
                {
                    context.Report(
                        ProgramAnalysisDiagnostics.ReadonlyContextAllocatesMutable, allocation.Node, [allocation],
                        TypeDisplay.Mist(allocation.Type), frame.Describe());
                }

                break;

            case PropertyReferenceExpression read when frame.IsReadonly && !targets.Contains(node):
                JudgeGetter(read.Property, read, frame, context, inference);
                break;

            case MemberAccessExpression { Member: PropertyResult { Property: { } accessed } } access
                when frame.IsReadonly && !targets.Contains(node):
                JudgeGetter(accessed, access, frame, context, inference);
                break;
        }
    }

    private static void JudgeGetter(
        Property property, Expression at, Frame frame, AnalysisContext context, ReadonlyInference inference)
    {
        if(!inference.IsReadonlyGetter(property))
        {
            context.Report(
                ProgramAnalysisDiagnostics.ReadonlyContextCallsMutable, at.Node, [at],
                property.Name, frame.Describe());
        }
    }

    private static void JudgeAssignment(
        AssignmentExpression assignment, Frame frame, AnalysisContext context, ReadonlyInference inference)
    {
        if(assignment.Target is MemberAccessExpression { Receiver: LocalReferenceExpression { Local.Parameter.Modifier.IsReadonly: true } parameter }
            && Written(assignment.Target) is { } through)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedOnReadonlyParameter, assignment.Target.Node,
                [assignment.Target], through.Name, parameter.Local.Name);
            return;
        }

        if(SetProperty(assignment.Target) is { } set)
        {
            if(frame.IsReadonly && !frame.MaySet(set, inference))
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

        if(frame.InReadonlyBlock)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyBlock, assignment.Target.Node,
                [assignment.Target], field.Name);
            return;
        }

        if(frame.MayAssign(field))
        {
            return;
        }

        if(frame.IsReadonly)
        {
            context.Report(
                ProgramAnalysisDiagnostics.FieldAssignedInReadonlyMember, assignment.Target.Node,
                [assignment.Target], field.Name, frame.MemberName());
        }
        else if(field.Modifiers.IsReadonly && frame.Member is Method and not Constructor)
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
            _ => null,
        };
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

    private sealed record Frame(object? Member, bool IsReadonly, bool Privileged, bool BackingFieldExempt, bool InReadonlyBlock)
    {
        public static Frame For(SourceRoot root)
        {
            return root.Member switch
            {
                Constructor constructor => new Frame(constructor, constructor.Modifiers.IsReadonly, true, false, false),
                Method method => new Frame(method, method.Modifiers.IsReadonly, false, false, false),
                Property property => new Frame(
                    property, property.Modifiers.IsReadonly, false, IsSetterBody(property.Setter, root.Root), false),
                Indexer indexer => new Frame(indexer, indexer.Modifiers.IsReadonly, false, false, false),
                _ => new Frame(root.Member, false, false, false, false),
            };
        }

        public Frame InsideReadonlyBlock()
        {
            return this with { IsReadonly = true, Privileged = false, InReadonlyBlock = true };
        }

        public bool MayAssign(Field field)
        {
            return Privileged
                && Member is Constructor { DeclaringType: { } built }
                && ReferenceEquals(field.DeclaringType, built);
        }

        public bool MaySet(Property property, ReadonlyInference inference)
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