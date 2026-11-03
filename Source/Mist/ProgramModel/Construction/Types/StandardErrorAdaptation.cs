using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Types;

internal static class StandardErrorAdaptation
{
    public static void Register(ConcreteType? adapter)
    {
        if(adapter is not { FullyQualifiedName: "Standard.ExceptionError", Assembly.Kind: MistAssemblyKind.Standard })
        {
            return;
        }

        var conversion = adapter.CastOperatorsList.SingleOrDefault(candidate => candidate.IsImplicit
            && candidate.SourceParameter.Type.FullyQualifiedName == "System.Exception"
            && ReferenceEquals(candidate.TargetType, adapter));
        if(conversion?.SourceParameter.Type is ConcreteType exception)
        {
            // The foreign source cannot declare Standard's conversion; retain its declared owner while exposing it to ordinary selection.
            exception.ErrorAdaptation = conversion;
        }
    }
}