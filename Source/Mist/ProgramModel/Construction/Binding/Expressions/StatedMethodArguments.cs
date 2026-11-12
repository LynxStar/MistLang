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
        var arguments = perCandidate is null || candidates.Any(candidate => !perCandidate.ContainsKey(candidate))
            ? context.Resolver.ResolveTypeArguments(stated, ctx.Scope)
            : null;
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
            StatedShared = arguments is not null,
            StatedPerCandidate = perCandidate,
        };
    }

    /// <summary>
    /// Only a candidate says whether a written position is a dependent slot, so a list is resolved once
    /// per distinct shape of slots and shared by the candidates of that shape, with each resolution's
    /// reports withheld for the call to settle once it has chosen. Only a call owns the captures, so a
    /// group named as a value reads no slot and takes the shared resolution alone.
    /// </summary>
    private static Dictionary<Method, StatedResolution>? ResolvePerCandidate(
        IReadOnlyList<Method> candidates, TypeArgumentListNode stated, int written, BindingContext ctx, BuilderContext context)
    {
        Dictionary<Method, StatedResolution>? resolved = null;
        var shapes = new List<(TypeParameter?[] Slots, StatedResolution Resolution)>();

        foreach(var candidate in candidates)
        {
            if(candidate.TypeParametersList.Count != written || !ScopeResolver.HasSlot(candidate.TypeParametersList, written))
            {
                continue;
            }

            var slots = candidate.TypeParametersList.Select(parameter => parameter is { IsDependent: true, IsPack: false } ? parameter : null).ToArray();
            var shape = shapes.Find(shape => shape.Slots.SequenceEqual(slots, ReferenceEqualityComparer.Instance));

            if(shape.Resolution is null)
            {
                var values = new SlotValueBinder(ctx, context);
                IReadOnlyList<IMistType> arguments = [];
                var withheld = context.Diagnostics.Withhold(
                    () => arguments = context.Resolver.ResolveTypeArguments(stated, ctx.Scope, candidate.TypeParametersList, values));
                shape = (slots, new StatedResolution(arguments, values.Captures, withheld));
                shapes.Add(shape);
            }

            resolved ??= new Dictionary<Method, StatedResolution>(ReferenceEqualityComparer.Instance);
            resolved[candidate] = shape.Resolution;
        }

        return resolved;
    }
}