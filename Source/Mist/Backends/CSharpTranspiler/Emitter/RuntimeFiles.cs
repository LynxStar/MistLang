using Mist.Backends.CSharpTranspiler.Lowering;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The runtime support files a program's bodies asked for, written after the bodies because only
/// writing them discovers which are needed.
/// </summary>
internal static class RuntimeFiles
{
    public static void Emit(string outputDir, List<string> emittedFiles)
    {
        Write(ContextLowering.Used(), "__Context.g.cs", ContextLowering.Emit, outputDir, emittedFiles);
        Write(LifetimeLowering.Used(), "__Ownership.g.cs", LifetimeLowering.Emit, outputDir, emittedFiles);
        Write(ArrayLowering.Used() || ArrayEnumeration.Used(), "__Array.g.cs", EmitArrays, outputDir, emittedFiles);
        Write(TaskCombinators.Used(), "__Tasks.g.cs", TaskCombinators.Emit, outputDir, emittedFiles);
        Write(InvariantCulture.Used(), "__Invariant.g.cs", InvariantCulture.Emit, outputDir, emittedFiles);

        using(TypeNameEmitter.Oblivious())
        {
            Write(StorageStamping.Used(), "__Stamping.g.cs", StorageStamping.Emit, outputDir, emittedFiles);
        }
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

    private static void Write(bool used, string name, Action<CSharpWriter> emit, string outputDir, List<string> emittedFiles)
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