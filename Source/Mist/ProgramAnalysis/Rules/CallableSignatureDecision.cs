using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

internal static class CallableSignatureDecision
{
    public static IEnumerable<CallableSignatureRepeat> Operators(IEnumerable<Operator> operators)
    {
        var seen = new Dictionary<(OperatorKind Kind, string Signature), Operator>();

        foreach(var op in operators)
        {
            if(op.DeclarationNode is null || OverloadSignature.Of(op, TypeSubstitution.Empty, null) is not { } signature)
            {
                continue;
            }

            var key = (op.Kind, signature);
            if(seen.TryGetValue(key, out var earlier))
            {
                yield return new CallableSignatureRepeat(op, earlier);
            }
            else
            {
                seen.Add(key, op);
            }
        }
    }

    public static IEnumerable<CallableSignatureRepeat> Casts(IEnumerable<CastOperator> casts)
    {
        var seen = new Dictionary<(bool IsImplicit, string Source, string Target), CastOperator>();

        foreach(var cast in casts)
        {
            if(cast.DeclarationNode is null
                || OverloadSignature.TypeOf(cast.SourceParameter.Type) is not { } source
                || OverloadSignature.TypeOf(cast.TargetType) is not { } target)
            {
                continue;
            }

            var key = (cast.IsImplicit, source, target);
            if(seen.TryGetValue(key, out var earlier))
            {
                yield return new CallableSignatureRepeat(cast, earlier);
            }
            else
            {
                seen.Add(key, cast);
            }
        }
    }
}

internal sealed record CallableSignatureRepeat(Method Later, Method Earlier);