using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;

namespace Mist.ProgramModel.Construction.Binding;

internal static class ProjectedWithholding
{
    public static ImmutableArray<Frame> Frames(MethodGroupExpression group, Scope? scope, TypeInterner interner)
    {
        return group.Receiver switch
        {
            null when scope is not null => Frames(scope, group.MemberName),
            null => [.. group.Candidates.Select(candidate => candidate.DeclaringType).OfType<IMistType>()
                .Distinct<IMistType>(ReferenceEqualityComparer.Instance).SelectMany(type => Frames(type, group.MemberName))],
            TypeReferenceExpression named => Frames(named.ReferencedType, group.MemberName),
            { } target => Frames(group.IsConditional
                ? OptionalLifting.NonVoidPortion(target.Type, interner) : target.Type, group.MemberName),
        };
    }

    public static ImmutableArray<Frame> Frames(Scope scope, string name)
    {
        var frames = ImmutableArray.CreateBuilder<Frame>();
        for(var current = scope; current is not null; current = current.Parent)
        {
            if(current is TypeScope type)
            {
                frames.AddRange(Frames(type.Type, name));
            }
        }

        return frames.ToImmutable();
    }

    public static ImmutableArray<Frame> Frames(IMistType type, string name)
    {
        var frames = ImmutableArray.CreateBuilder<Frame>();
        var pending = new Queue<IMistType>();
        var seen = new HashSet<IMistType>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(type);

        while(pending.TryDequeue(out var current))
        {
            current = current is ConstructedType constructed ? constructed.Definition : current;
            if(!seen.Add(current))
            {
                continue;
            }

            if(current is MemberType member)
            {
                frames.AddRange(member.WithheldMembers.Where(frame => frame.Member.Spelling == name));
                if(member is ConcreteType { BaseType: { } concreteBase })
                {
                    pending.Enqueue(concreteBase);
                }

                foreach(var abstractBase in member.BaseAbstractTypes.Where(abstractBase => TypeMemberLookup.Searches(member, abstractBase)))
                {
                    pending.Enqueue(abstractBase);
                }
            }

            if(current is TypeParameter parameter)
            {
                foreach(var constraint in parameter.ConstraintsList.OfType<TypeConstraint>())
                {
                    pending.Enqueue(constraint.ConstraintType);
                }
            }

            if(current is Newtype { SourceType: { } source })
            {
                pending.Enqueue(source);
            }
        }

        return frames.ToImmutable();
    }

    public static void Report(
        DiagnosticBag diagnostics, MistDiagnostic descriptor, SyntaxNode node,
        ImmutableArray<Frame> frames, params ReadOnlySpan<SlotValue> arguments)
    {
        var origin = DiagnosticHelper.ResolveLocation(node);
        diagnostics.AdmitOwned(node, DiagnosticFactory.Create(
            descriptor, origin with { Frames = [.. origin.Frames, .. frames] }, diagnostics.Stage, arguments));
    }
}