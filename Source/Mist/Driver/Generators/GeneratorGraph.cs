using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver.Generators;

public static class GeneratorGraph
{
    public static ImmutableArray<GeneratorNode> Order(GeneratorNode root,
        Func<string, ProjectConfiguration?> loadGenerator,
        Func<GeneratorNode, IEnumerable<GeneratorNode>> ordinaryProjects,
        DiagnosticBag diagnostics)
    {
        var nodes = new Dictionary<string, GeneratorNode>(StringComparer.Ordinal) { [root.Manifest] = root };
        var done = new HashSet<string>(StringComparer.Ordinal);
        var active = new List<(string Manifest, bool GeneratorEdge)>();
        var result = ImmutableArray.CreateBuilder<GeneratorNode>();
        var reported = false;
        visit(root, false);
        return result.ToImmutable();

        void visit(GeneratorNode node, bool generatorEdge)
        {
            var cycle = active.FindIndex(item => item.Manifest == node.Manifest);
            if(cycle >= 0)
            {
                if(!reported && (generatorEdge || active.Skip(cycle + 1).Any(item => item.GeneratorEdge)))
                {
                    diagnostics.ReportTyped(DriverDiagnostics.GeneratorCycle, new ProjectLocation(active[^1].Manifest, null),
                        new SequenceValue(SlotKind.Path, [.. active.Skip(cycle).Select(item => (SlotValue)new PathValue(item.Manifest)), new PathValue(node.Manifest)], " → "));
                    reported = true;
                }

                return;
            }

            if(done.Contains(node.Manifest))
            {
                return;
            }

            active.Add((node.Manifest, generatorEdge));
            foreach(var ordinary in ordinaryProjects(node))
            {
                nodes[ordinary.Manifest] = ordinary;
            }

            foreach(var reference in node.Configuration.References)
            {
                if(reference.Kind == ReferenceKind.MistProject && nodes.TryGetValue(reference.Path, out var ordinary))
                {
                    visit(ordinary, false);
                }
            }

            if(!node.Configuration.Generators.IsDefaultOrEmpty)
            {
                foreach(var generator in node.Configuration.Generators)
                {
                    if(!nodes.TryGetValue(generator.ProjectPath, out var dependency))
                    {
                        if(loadGenerator(generator.ProjectPath) is not { } config)
                        {
                            continue;
                        }

                        dependency = new GeneratorNode(generator.ProjectPath, config);
                        nodes.Add(dependency.Manifest, dependency);
                    }

                    visit(dependency, true);
                }
            }

            active.RemoveAt(active.Count - 1);
            _ = done.Add(node.Manifest);
            result.Add(node);
        }
    }
}