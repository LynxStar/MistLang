namespace Mist.ProgramModel.Contracts;

/// <summary>
/// Projects the types of loaded .NET assemblies into the program model alongside source-declared ones.
/// </summary>
public interface IAmbientTypeProvider
{
    /// <summary>
    /// Runs after the source type shells are registered (Phase 1) and before member resolution (Phase 2).
    /// </summary>
    void BuildAndRegister(Model.ProgramModel programModel);
}