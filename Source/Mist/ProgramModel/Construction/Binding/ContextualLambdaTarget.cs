using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

internal static class ContextualLambdaTarget
{
    // Only a lambda's destination may supply its present signature; an optional value is not callable.
    public static IFunctionType? Of(IMistType? target, TypeInterner interner)
    {
        var expanded = AliasTransparency.Expand(target ?? ErrorType.Instance, interner);
        var present = OptionalUnion.InnerType(expanded) ?? expanded;
        return DelegateShape.Of(AliasTransparency.Expand(present, interner));
    }
}