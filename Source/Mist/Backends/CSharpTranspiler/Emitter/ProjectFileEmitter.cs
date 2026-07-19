using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Generates a minimal .csproj file for the emitted C# project.
/// </summary>
internal static class ProjectFileEmitter
{
    public static string Emit(CompilerOptions options)
    {
        var outputType = options.OutputType == ProjectOutputType.Executable ? "Exe" : "Library";

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>{options.TargetFramework}</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Deterministic>true</Deterministic>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
              </PropertyGroup>
            </Project>
            """;
    }
}