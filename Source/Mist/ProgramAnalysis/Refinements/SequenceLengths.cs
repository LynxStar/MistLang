using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

internal static class SequenceLengths
{
    public static SequenceLengthOperation Of(InvocationExpression invocation, Expression? receiver)
    {
        if(invocation.Method is not { } method || receiver is null
            || !Lengths.IsSized(receiver.Type) || !Lengths.IsSized(invocation.Type))
        {
            return SequenceLengthOperation.None;
        }

        if(method.SequenceLengthOperation is not SequenceLengthOperation.None)
        {
            return method.SequenceLengthOperation;
        }

        if(!method.Modifiers.IsIntrinsic || method.DeclaringType is not { Modifiers.IsIntrinsic: true } host
            || !ReferenceEquals(Definition(receiver.Type), host))
        {
            return SequenceLengthOperation.None;
        }

        if(host.FullyQualifiedName == "Standard.String" && method.Name == "ToCharArray"
            && method.ParametersList.Count == 0 && invocation.Arguments.Count == 0)
        {
            return SequenceLengthOperation.Preserved;
        }

        if(IsSpan(host) && method.Name == "Slice" && method.ParametersList.Count == 2
            && invocation.Arguments.Count == 2 && ReferenceEquals(Definition(invocation.Type), host))
        {
            return SequenceLengthOperation.Slice;
        }

        return SequenceLengthOperation.None;
    }

    public static bool CopiesArray(NewExpression creation)
    {
        return creation is { Constructor: { Modifiers.IsIntrinsic: true, DeclaringType: { Modifiers.IsIntrinsic: true } host }, Arguments: [var source] }
            && IsSpan(host) && ReferenceEquals(Definition(creation.Type), host)
            && Requirement.Unrefined(source.Type) is ConstructedType { Definition.FullyQualifiedName: "Standard.Array<,>", TypeArguments: [var sourceElement, _] }
            && Requirement.Unrefined(creation.Type) is ConstructedType { TypeArguments: [var resultElement, _] }
            && ReferenceEquals(sourceElement, resultElement);
    }

    private static IMistType Definition(IMistType type)
    {
        var unrefined = Requirement.Unrefined(type);
        return unrefined is ConstructedType constructed ? constructed.Definition : unrefined;
    }

    private static bool IsSpan(IMistType type)
    {
        return type.FullyQualifiedName is "Standard.Span<,>" or "Standard.ReadOnlySpan<,>";
    }
}