using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

internal static class ParenthesesMethodBodies
{
    public static ImmutableDictionary<string, string> Emit(IEnumerable<string> statements, string locals, string types = "")
    {
        var cases = statements.Distinct(StringComparer.Ordinal).Select((statement, index) => (Statement: statement, Name: $"Case{index}")).ToArray();
        var methods = string.Join("\n", cases.Select(c => $"public static void {c.Name}() {{\n{locals}\n{c.Statement}\n}}"));
        var model = StandardModelProbe.Build("", out var built, $$"""
            {{types}}
            public class Cases
            {
                {{methods}}
            }
            """);
        Assert.Empty(built.Where(d => d.Severity == DiagnosticSeverity.Error));
        var owner = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Cases"]);
        var boundMethods = owner.MethodsList.ToDictionary(method => method.Name, StringComparer.Ordinal);
        var bodies = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        using var supply = IntrinsicSupply.Enter(model, Type.GetType);
        using var promotion = AsyncPromotion.Enter(model);
        foreach(var (statement, name) in cases)
        {
            var method = boundMethods[name];
            var writer = new CSharpWriter();
            var diagnostics = new DiagnosticBag();
            Assert.NotNull(method.Body);
            BodyEmitter.EmitMethodBody(method, method.Body, writer, diagnostics);
            Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));
            bodies.Add(statement, writer.ToString());
        }

        return bodies.ToImmutable();
    }
}