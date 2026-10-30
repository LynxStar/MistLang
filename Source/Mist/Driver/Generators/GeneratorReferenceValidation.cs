using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver.Generators;

internal static class GeneratorReferenceValidation
{
    public static void Validate(GeneratorNode node, IReadOnlyDictionary<string, ProjectConfiguration> configurations, DiagnosticBag diagnostics)
    {
        foreach(var reference in node.Configuration.References)
        {
            if(reference.Kind == ReferenceKind.MistProject && configurations.TryGetValue(reference.Path, out var target)
                && target.ProjectType == MistProjectType.SourceGenerator)
            {
                report(reference.Name, "source generators must use source_generators edges");
            }
        }

        if(!node.Configuration.Generators.IsDefaultOrEmpty)
        {
            foreach(var reference in node.Configuration.Generators)
            {
                if(configurations.TryGetValue(reference.ProjectPath, out var target) && target.ProjectType != MistProjectType.SourceGenerator)
                {
                    report(reference.Name, "target is not a source_generator project");
                }
            }
        }

        void report(string name, string reason)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorReferenceInvalid, new ProjectLocation(node.Manifest, null), new NameValue(name), new TextValue(reason));
        }
    }
}