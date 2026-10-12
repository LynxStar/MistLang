using System.Reflection;
using System.Text.RegularExpressions;
using Mist.Driver.Contracts;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Helpers;

/// <summary>
/// An <see cref="IAmbientTypeSource"/> for seam tests: projects nothing into the model, and answers
/// the CLR type behind a Standard intrinsic from the test host's own runtime assemblies, which carry
/// the public surface the emitter's intrinsic audit reads off the reference pack. A seam program that
/// prints therefore compiles without an assembly being loaded.
/// </summary>
internal sealed class StubAmbientTypeSource : IAmbientTypeSource
{
    private static readonly Assembly[] RuntimeAssemblies =
        [typeof(object).Assembly, typeof(Console).Assembly, typeof(Regex).Assembly];

    public static StubAmbientTypeSource Instance { get; } = new();

    public void BuildAndRegister(ProgramModelClass programModel)
    {
    }

    public Type? FindClrType(string fullName)
    {
        return RuntimeAssemblies.Select(assembly => assembly.GetType(fullName)).FirstOrDefault(type => type is not null);
    }

    public void Dispose()
    {
    }
}