using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static class StatedMethodArguments
{
    public static Expression Bind(
        Expression member, TypeArgumentListNode stated, string name, SyntaxNode node, BindingContext ctx, BuilderContext context)
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

        var arguments = context.Resolver.ResolveTypeArguments(stated, ctx.Scope);
        var spreads = Enumerable.Range(0, arguments.Count).Where(index => arguments[index] is SpreadType).ToList();
        var candidates = group.Candidates;
        if(spreads.Count > 0)
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
                    context.Diagnostics.ReportAtNodeTyped(ProgramModelDiagnostics.SpreadNonPackArgument, anchor, null, new NameValue(spread.Operand.Name), new NameValue(name));
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
            StatedTypeArguments = arguments,
        };
    }
}