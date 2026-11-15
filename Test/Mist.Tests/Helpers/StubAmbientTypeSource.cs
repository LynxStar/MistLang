using System.Reflection;
using System.Text.RegularExpressions;
using Mist.Driver.Contracts;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

/// <summary>
/// Answers CLR types from the test host's runtime assemblies, which carry the public surface the emitter's
/// intrinsic audit reads off the reference pack, so a seam program that prints compiles without an
/// assembly being loaded.
/// </summary>
internal sealed class StubAmbientTypeSource : IAmbientTypeSource
{
    private static readonly Assembly[] RuntimeAssemblies =
        [typeof(object).Assembly, typeof(Console).Assembly, typeof(Regex).Assembly];

    public static StubAmbientTypeSource Instance { get; } = new();

    public void BuildAndRegister(ProgramModelClass programModel)
    {
        StandardInteropAmbient.RegisterException(programModel);
    }

    public Type? FindClrType(string fullName)
    {
        return RuntimeAssemblies.Select(assembly => assembly.GetType(fullName)).FirstOrDefault(type => type is not null);
    }

    public void Dispose()
    {
    }
}