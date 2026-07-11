using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.Tests.Helpers;

/// <summary>
/// A no-op <see cref="IAmbientTypeProvider"/> that resolves nothing.
/// Used by tests that exercise the program model builder without
/// needing assembly resolution.
/// </summary>
internal sealed class NullAmbientTypeProvider : IAmbientTypeProvider
{
    /// <summary>
    /// Shared singleton instance.
    /// </summary>
    public static NullAmbientTypeProvider Instance { get; } = new();

    /// <inheritdoc />
    public IMistType? ResolveType(string namespaceName, string typeName, ProgramModel programModel)
    {
        return null;
    }
}
