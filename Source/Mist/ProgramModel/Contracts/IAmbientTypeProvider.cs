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
}
