namespace Mist.ProgramModel.Contracts;

/// <summary>
/// Resolves types from loaded .NET assemblies into IMistType instances
/// that live in the ProgramModel alongside source-declared types.
/// </summary>
public interface IAmbientTypeProvider
{
    /// <summary>
    /// Eagerly builds and registers all ambient types into the ProgramModel.
    /// Called after source type shells are registered (Phase 1) but before
    /// member resolution (Phase 2). After this call, all ambient types are
    /// visible in the ProgramModel's namespace dictionary.
    /// </summary>
    void BuildAndRegister(Model.ProgramModel programModel);

    /// <summary>
    /// Adds the CLR members of the bridged well-known types to the Standard declarations standing in
    /// for them. Separate from <see cref="BuildAndRegister"/> and called after member resolution
    /// BECAUSE IT HAS TO MERGE WITH WHAT SOURCE DECLARED, which does not exist yet at Phase 1b: at
    /// that point Standard.String holds no members at all, so there is nothing to merge against and
    /// the projected copy of every colliding member would win by arriving first.
    /// </summary>
    void BridgeWellKnownMembers(Model.ProgramModel programModel);
}