using Mist.Backends.CSharpTranspiler.Lowering;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal sealed partial class CSharpEmitterV2
{
    private static void EmitRuntimes(string outputDir, List<string> emittedFiles)
    {
        EmitRuntime(outputDir, emittedFiles, "__Context.g.cs", ContextLowering.Used(), ContextLowering.Emit);
        EmitRuntime(outputDir, emittedFiles, "__Ownership.g.cs", LifetimeLowering.Used(), LifetimeLowering.Emit);
        EmitRuntime(outputDir, emittedFiles, "__Array.g.cs", ArrayLowering.Used() || ArrayEnumeration.Used(), EmitArrays);
        EmitRuntime(outputDir, emittedFiles, "__Statics.g.cs", StaticHolders.Used(), StaticHolders.Emit);
        EmitRuntime(outputDir, emittedFiles, "__Closings.g.cs", ClosingWitnesses.Used(), ClosingWitnesses.Emit);
        EmitRuntime(outputDir, emittedFiles, "__Tasks.g.cs", TaskCombinators.Used(), TaskCombinators.Emit);
        EmitRuntime(outputDir, emittedFiles, "__Invariant.g.cs", InvariantCulture.Used(), InvariantCulture.Emit);
    }

    private static void EmitArrays(CSharpWriter writer)
    {
        if(ArrayLowering.Used())
        {
            ArrayLowering.Emit(writer);
        }

        if(ArrayEnumeration.Used())
        {
            if(ArrayLowering.Used())
            {
                writer.WriteBlankLine();
            }

            ArrayEnumeration.Emit(writer);
        }
    }

    private static void EmitVoidStandIn(string outputDir, List<string> emittedFiles)
    {
        EmitRuntime(outputDir, emittedFiles, "__Void.g.cs", used: true, VoidStandIn.Emit);
    }

    private static void EmitRuntime(string outputDir, List<string> emittedFiles, string name, bool used, Action<CSharpWriter> emit)
    {
        if(!used)
        {
            return;
        }

        var writer = new CSharpWriter();
        emit(writer);

        var path = Path.Combine(outputDir, name);
        File.WriteAllText(path, writer.ToString());
        emittedFiles.Add(path);
    }
}