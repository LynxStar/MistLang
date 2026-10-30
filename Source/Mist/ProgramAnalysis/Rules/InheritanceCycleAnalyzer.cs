using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class InheritanceCycleAnalyzer : IProgramAnalyzer
{
    public string Id => "inheritance-cycle-checks";

    public void Analyze(AnalysisContext context)
    {
        Report(context, Decide(context.Types));
    }

    internal static IReadOnlyList<InheritanceCycle> Decide(IEnumerable<MistType> types)
    {
        var complete = new HashSet<MemberType>();
        var active = new HashSet<MemberType>();
        var cycles = new List<InheritanceCycle>();
        var stack = new Stack<Frame>();

        foreach(var root in types.OfType<MemberType>())
        {
            if(complete.Contains(root))
            {
                continue;
            }

            _ = active.Add(root);
            stack.Push(new Frame(root));

            while(stack.TryPeek(out var frame))
            {
                if(frame.Next == frame.Bases.Count)
                {
                    _ = active.Remove(frame.Type);
                    _ = complete.Add(frame.Type);
                    _ = stack.Pop();
                    continue;
                }

                var inherited = frame.Bases[frame.Next++];
                var definition = inherited is ConstructedType constructed ? constructed.Definition : inherited;
                if(definition is not MemberType member || complete.Contains(member))
                {
                    continue;
                }

                if(!active.Add(member))
                {
                    cycles.Add(new InheritanceCycle(frame.Type, inherited, Site(frame.Type, inherited)));
                    continue;
                }

                stack.Push(new Frame(member));
            }
        }

        return cycles;
    }

    internal static void Report(AnalysisContext context, IReadOnlyList<InheritanceCycle> cycles)
    {
        foreach(var cycle in cycles)
        {
            if(cycle.Node is { } node)
            {
                context.Report(ProgramAnalysisDiagnostics.CyclicInheritance, node, [],
                    DiagnosticSnapshot.Type(cycle.Owner), DiagnosticSnapshot.Type(cycle.Base));
            }
            else
            {
                context.ReportAtProject(ProgramAnalysisDiagnostics.CyclicInheritance, cycle.Owner.Assembly,
                    DiagnosticSnapshot.Type(cycle.Owner), DiagnosticSnapshot.Type(cycle.Base));
            }
        }
    }

    private static SyntaxNode? Site(MemberType owner, MistType inherited)
    {
        if(owner.DeclarationNode is BodyTypeTypeDeclarationNode { TypeBody.BaseList: { } bases })
        {
            return owner is ConcreteType concrete && ReferenceEquals(concrete.BaseType, inherited)
                ? bases.First
                : bases;
        }

        return owner.DeclarationNode;
    }

    private sealed class Frame
    {
        public MemberType Type { get; }

        public List<MistType> Bases { get; } = [];

        public int Next { get; set; }

        public Frame(MemberType type)
        {
            Type = type;
            if(type is ConcreteType { BaseType: { } concreteBase })
            {
                Bases.Add(concreteBase);
            }

            Bases.AddRange(type.BaseAbstractTypes);
        }
    }
}

internal sealed record InheritanceCycle(MemberType Owner, MistType Base, SyntaxNode? Node);