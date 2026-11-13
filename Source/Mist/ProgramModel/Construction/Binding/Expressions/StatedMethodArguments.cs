using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal sealed record StatedResolution(IReadOnlyList<IMistType> Arguments, IReadOnlyList<SlotCapture> Captures, Withheld? Withheld);

internal static class StatedMethodArguments
{
    public static Expression Bind(
        Expression member, TypeArgumentListNode stated, string name, SyntaxNode node, BindingContext ctx, BuilderContext context, bool ownsCaptures = true)
    {
        if(member is ErrorExpression)
        {
            return member;
        }

        if(member is not MethodGroupExpression group)
        {
            context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.TypeArgumentsOnANonMethod, node, null, new NameValue(name));
            return new ErrorExpression(node);
        }

        var written = 1 + stated.TypeArguments.Length;
        var candidates = group.Candidates;
        var perCandidate = ownsCaptures ? ResolvePerCandidate(candidates, stated, written, ctx, context) : null;
        IReadOnlyList<IMistType>? arguments = null;
        Withheld? shared = null;
        if(perCandidate is null)
        {
            arguments = context.Resolver.ResolveTypeArguments(stated, ctx.Scope);
        }
        else if(candidates.Any(candidate => !perCandidate.ContainsKey(candidate) && PackBinding.AbsorbingSlot(candidate.TypeParametersList) is not null)
            || perCandidate.Values.Any(resolved => resolved.Arguments.Any(argument => argument is SpreadType)))
        {
            shared = context.Diagnostics.Withhold(() => arguments = context.Resolver.ResolveTypeArguments(stated, ctx.Scope));
            if(arguments!.Any(argument => argument is SpreadType))
            {
                shared.Commit();
            }
        }

        if(perCandidate is not null)
        {
            candidates = Taking(candidates, perCandidate, shared is null ? null : arguments);
        }

        var shape = arguments ?? perCandidate!.Values.First().Arguments;
        var spreads = Enumerable.Range(0, shape.Count).Where(index => shape[index] is SpreadType).ToList();
        if(spreads.Count > 0 && arguments is not null)
        {
            var byArity = candidates.Where(candidate => candidate.TypeParametersList.Count == arguments.Count).ToList();
            if(byArity.Count > 0)
            {
                candidates = byArity.Where(candidate => spreads.All(index =>
                    PackBinding.AcceptsSpread(candidate.TypeParametersList, arguments.Count, index))).ToList();
                if(candidates.Count == 0)
                {
                    var rejected = byArity[0];
                    var index = spreads.First(index => !PackBinding.AcceptsSpread(rejected.TypeParametersList, arguments.Count, index));
                    var argumentNodes = new[] { stated.TypeArgument }.Concat(stated.TypeArguments).ToList();
                    var anchor = argumentNodes[index] is TypeReferenceTypeArgumentTypeArgumentNode type
                        ? type.Type.First : (SyntaxNode)argumentNodes[index];
                    var spread = (SpreadType)arguments[index];
                    ProjectedWithholding.Report(context.Diagnostics, ProgramModelDiagnostics.SpreadNonPackArgument, anchor,
                        ProjectedWithholding.Frames(group, ctx.Scope, context.ProgramModel.TypeInterner), new NameValue(spread.Operand.Name), new NameValue(name));
                    return new ErrorExpression(node, [group]);
                }

                var parameters = candidates[0].TypeParametersList;
                var absorbing = PackBinding.AbsorbingSlot(parameters)!.Value;
                var domain = parameters[absorbing].Domain
                    ?? context.Resolver.ConstructArray(context.Resolver.LookupWellKnown(WellKnownTypes.Type), length: null);
                arguments = PackBinding.Bind(parameters, arguments, absorbing, context.ProgramModel.TypeInterner, domain)!;
            }
        }

        return new MethodGroupExpression(group.Node, group.Receiver, group.MemberName, candidates, group.ReceiverConstructed, group.IsConditional)
        {
            Paths = group.Paths,
            StatedTypeArguments = arguments ?? shape,
            StatedShared = shared,
            StatedPerCandidate = perCandidate,
        };
    }

    /// <summary>
    /// A candidate reads the stated list by the kind of its own slots, so one that reads a type where it
    /// has a dependent slot, a value where it has an ordinary one, or a name that is neither does not
    /// take the list while another does, and leaves the call with its reports. A pack candidate reads it
    /// as the shared resolution does, every name a type. With none taking it the group is kept whole, so
    /// a single declaration is refused as it is without an overload.
    /// </summary>
    private static IReadOnlyList<Method> Taking(
        IReadOnlyList<Method> candidates, Dictionary<Method, StatedResolution> perCandidate, IReadOnlyList<IMistType>? shared)
    {
        bool judged(Method candidate)
        {
            return perCandidate.ContainsKey(candidate)
                ? PackBinding.AbsorbingSlot(candidate.TypeParametersList) is null
                : shared is not null && PackBinding.AbsorbingSlot(candidate.TypeParametersList) is not null;
        }

        bool takes(Method candidate)
        {
            return perCandidate.TryGetValue(candidate, out var resolved)
                ? candidate.TypeParametersList.Zip(resolved.Arguments).All(pair => pair.Second is not ErrorType
                    && pair.First.IsDependent == (pair.Second is CapturedValue or LiftedType or UnknownValueType or TypeParameter { IsDependent: true }))
                : !shared!.Any(argument => argument is ErrorType);
        }

        var refusing = candidates.Where(candidate => judged(candidate) && !takes(candidate)).ToList();
        if(refusing.Count == 0 || !candidates.Any(candidate => judged(candidate) && takes(candidate)))
        {
            return candidates;
        }

        foreach(var candidate in refusing)
        {
            if(perCandidate.Remove(candidate, out var resolved) && resolved.Withheld is { Decided: false } withheld)
            {
                withheld.Discard();
            }
        }

        return [.. candidates.Except(refusing)];
    }

    /// <summary>
    /// Only a candidate says whether a written position is a dependent slot, so a list is resolved once
    /// per distinct shape of slots and shared by the candidates of that shape, with each resolution's
    /// reports withheld for the call to settle once it has chosen. A candidate without a slot reads it
    /// as the shared resolution does, with no value binder. Only a call owns the captures, so a group
    /// named as a value reads no slot and takes the shared resolution alone.
    /// </summary>
    private static Dictionary<Method, StatedResolution>? ResolvePerCandidate(
        IReadOnlyList<Method> candidates, TypeArgumentListNode stated, int written, BindingContext ctx, BuilderContext context)
    {
        bool slotted(Method candidate)
        {
            return candidate.TypeParametersList.Count == written && ScopeResolver.HasSlot(candidate.TypeParametersList, written);
        }

        if(!candidates.Any(slotted))
        {
            return null;
        }

        var resolved = new Dictionary<Method, StatedResolution>(ReferenceEqualityComparer.Instance);
        var shapes = new List<(TypeParameter?[] Slots, StatedResolution Resolution)>();

        foreach(var candidate in candidates.OrderBy(candidate => !slotted(candidate)))
        {
            if(candidate.TypeParametersList.Count != written
                || (!slotted(candidate) && PackBinding.AbsorbingSlot(candidate.TypeParametersList) is not null))
            {
                continue;
            }

            var slots = candidate.TypeParametersList.Select(parameter => parameter is { IsDependent: true, IsPack: false } ? parameter : null).ToArray();
            var shape = shapes.Find(shape => shape.Slots.SequenceEqual(slots, ReferenceEqualityComparer.Instance));

            if(shape.Resolution is null)
            {
                var values = slotted(candidate) ? new SlotValueBinder(ctx, context) : null;
                IReadOnlyList<IMistType> arguments = [];
                var withheld = context.Diagnostics.Withhold(() => arguments = values is null
                    ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
                    : context.Resolver.ResolveTypeArguments(stated, ctx.Scope, candidate.TypeParametersList, values));
                shape = (slots, new StatedResolution(arguments, values?.Captures ?? [], withheld));
                shapes.Add(shape);
            }

            resolved[candidate] = shape.Resolution;
        }

        return resolved;
    }
}