using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// A construction over a type ALIAS, expanded to what it aliases.
/// <para>
/// AN ALIAS SHOULD NEVER SURVIVE RESOLUTION -- <see cref="ScopeResolver.ExpandAlias"/> expands every
/// one a source reference names. What reaches here comes from the AMBIENT projection instead, which
/// runs at Phase 1b, before Phase 2a has resolved any alias: a projected `System.Action` maps onto
/// Standard.Action, whose target is still null at that moment, so the projector can only construct
/// over the alias itself. By the time a body binds, the alias is resolved and this can finish the
/// job.
/// </para>
/// </summary>
internal static class AliasTransparency
{
    public static IMistType Expand(IMistType type, TypeInterner interner)
    {
        if(type is not ConstructedType { Definition: TypeAlias { AliasedType: { } aliased } alias } constructed)
        {
            return type;
        }

        var parameters = alias.TypeParametersList;
        var arguments = constructed.TypeArguments;

        var bound = PackBinding.AbsorbingSlot(parameters) is { } absorbing
            && parameters[absorbing].Domain is { } domain
                ? PackBinding.Bind(parameters, arguments, absorbing, interner, domain) ?? arguments
                : arguments;

        return TypeSubstitution.ForParameters(parameters, bound).Apply(aliased, interner);
    }
}