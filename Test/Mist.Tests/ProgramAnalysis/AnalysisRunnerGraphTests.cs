using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class AnalysisRunnerGraphTests
{
    // Only the runner decides which bodies a compile registers and where, so this is the whole phase over a hand-built model.
    [Fact]
    [Trait("Tier", "3")]
    public void TheRunnerLeavesEveryBodyOfTheModelInItsCompilesGraphs()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Probe", isLibrary: true);
        var origin = new ParsedOrigin(new SourceText("source.mist", new string(' ', 200)));
        var declaration = new BodyTypeTypeDeclarationNode { Span = new Storm.Runtime.SourceSpan(0, 100), Origin = origin };
        var modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var type = new ClassType("Tiger", "Probe", new Scope(null), null, declaration) { Modifiers = modifiers, Assembly = assembly };
        var parameter = new LocalVariable("n", type);
        var functionBody = new Block(new BlockNode { Span = new Storm.Runtime.SourceSpan(20, 10), Origin = origin });
        var function = new LocalFunctionStatement(new Method("Inner", type, [], default) { Body = functionBody }, [parameter]);
        var body = new Block(new BlockNode { Span = new Storm.Runtime.SourceSpan(10, 50), Origin = origin }) { Statements = [function] };
        type.MethodsList.Add(new Method("Run", type, [], modifiers) { DeclaringType = type, Body = body, Scope = new MethodScope(type.Scope) });
        var unit = new CompilationUnit { SourcePath = "source.mist" };
        unit.Types.Add(type);
        assembly.CompilationUnits.Add(unit);
        _ = model.RegisterType(type);

        ProgramAnalysisRunner.Run(model, new DiagnosticBag());

        using(FlowGraphs.Enter(model))
        {
            var nested = FlowGraphs.Shared.GraphOf(functionBody);

            Assert.Contains(nested.Entry.Steps, step => step.Kind == FlowStepKind.Introduce && ReferenceEquals(step.Variable, parameter));
            Assert.Same(nested, FlowGraphs.Shared.GraphOf(body).Nested[function]);
        }
    }
}