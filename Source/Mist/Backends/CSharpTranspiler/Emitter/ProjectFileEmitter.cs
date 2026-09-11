using System.Collections.Immutable;
using System.Security;
using System.Text;
using Mist.Driver.Contracts;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Generates the .csproj file for the emitted C# project.
/// </summary>
internal static class ProjectFileEmitter
{
    public static string Emit(CompilerOptions options, bool carriesEntryPoint)
    {
        var outputType = options.OutputType == ProjectOutputType.Executable ? "Exe" : "Library";

        var sections = string.Concat(
            EmitEntryPointSuppression(options.OutputType, carriesEntryPoint),
            EmitPropertyGroup(options.CsprojProperties),
            EmitItemGroup(options.Packages, p => $"""<PackageReference Include="{p.Name}" Version="{p.Version}" />"""),
            EmitItemGroup(options.FrameworkReferences, f => $"""<FrameworkReference Include="{f.Name}" />"""));

        return $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>{outputType}</OutputType>
                <TargetFramework>{options.TargetFramework}</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Deterministic>true</Deterministic>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
              </PropertyGroup>{sections}
            </Project>
            """;
    }

    /// <summary>
    /// Every assembly lands in one CLR assembly, so a library that composes an executable
    /// carries that executable's <c>Main</c>. The test SDK synthesizes a second one unless
    /// told not to, and the two collide as CS0017 -- but told not to when the sources carry
    /// none, it still turns the output into an executable, and that has no entry point at all
    /// (CS5001), which is what a plain test project is. So the suppression follows the
    /// <c>Main</c>. Splitting emission per assembly removes the need for this; see
    /// <c>backlog/multi-project-emission.md</c>.
    /// </summary>
    private static string EmitEntryPointSuppression(ProjectOutputType outputType, bool carriesEntryPoint)
    {
        if(outputType != ProjectOutputType.Library || !carriesEntryPoint)
        {
            return "";
        }

        return "\n  <PropertyGroup>\n    <GenerateProgramFile>false</GenerateProgramFile>\n  </PropertyGroup>";
    }

    private static string EmitPropertyGroup(ImmutableArray<CsprojProperty> properties)
    {
        if(properties.IsDefaultOrEmpty)
        {
            return "";
        }

        var builder = new StringBuilder("\n  <PropertyGroup>");
        foreach(var property in properties)
        {
            var value = SecurityElement.Escape(property.Value) ?? property.Value;
            _ = builder.Append($"\n    <{property.Key}>{value}</{property.Key}>");
        }

        _ = builder.Append("\n  </PropertyGroup>");
        return builder.ToString();
    }

    private static string EmitItemGroup<T>(ImmutableArray<T> items, Func<T, string> element)
    {
        if(items.IsDefaultOrEmpty)
        {
            return "";
        }

        var builder = new StringBuilder("\n  <ItemGroup>");
        foreach(var item in items)
        {
            _ = builder.Append($"\n    {element(item)}");
        }

        _ = builder.Append("\n  </ItemGroup>");
        return builder.ToString();
    }
}