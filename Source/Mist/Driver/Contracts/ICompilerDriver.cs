namespace Mist.Driver.Contracts;

public interface ICompilerDriver
{
    CompilerResult Compile(CompilerOptions options);
}