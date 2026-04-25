namespace Mist.Driver.Contracts;

/// <summary>
/// Contract for the top-level compiler driver that orchestrates the compilation pipeline.
/// </summary>
public interface ICompilerDriver
{
    CompilerResult Compile(CompilerOptions options);
}
