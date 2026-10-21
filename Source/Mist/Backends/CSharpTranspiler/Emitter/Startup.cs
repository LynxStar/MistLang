using Mist.Driver.Contracts;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// What the emitted program starts at: the one file whose top-level statements are written, or the
/// type Roslyn is told holds <c>Main</c>. Never both, because C# finds a top-level-statement entry
/// point itself and refuses a named one beside it; and neither for a library. Every assembly lands in
/// one CLR assembly, so a composed program holds a <c>Main</c> per project and naming the compiled
/// project's own is what keeps Roslyn from reporting CS0017.
/// </summary>
internal sealed record Startup(CompilationUnit? TopLevelUnit, string? MainTypeName)
{
    public static Startup? Of(IProgramModel programModel, CompilerOptions options)
    {
        var selection = programModel.SelfAssembly is MistAssembly self
            ? EntryPointSelection.Of(self)
            : new EntryPointSelection.None();

        switch(selection)
        {
            case EntryPointSelection.Refused:
                return null;

            case EntryPointSelection.None when options.OutputType == ProjectOutputType.Executable:
                return null;

            case EntryPointSelection.TopLevelStatements statements:
                var unit = programModel.SelfAssembly?.CompilationUnits.First(unit => unit.SourcePath == statements.File);
                return new Startup(unit, null);

            case EntryPointSelection.Main main:
                return new Startup(null, main.Class);

            default:
                return new Startup(null, null);
        }
    }
}