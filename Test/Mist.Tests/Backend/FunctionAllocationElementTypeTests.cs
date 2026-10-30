using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class FunctionAllocationElementTypeTests : ProgramModelProbe
{
    public FunctionAllocationElementTypeTests()
        : base("MistFunctionAllocation_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("new", "func<int, int>")]
    [InlineData("new", "Func<int, int>")]
    [InlineData("new", "Mapper<int>")]
    [InlineData("stackalloc", "func<int, int>")]
    [InlineData("stackalloc", "Func<int, int>")]
    [InlineData("stackalloc", "Mapper<int>")]
    public void FunctionElementsShareTheOrdinaryTypeReferencesInternedIdentity(string allocationKind, string elementName)
    {
        var model = Build(Source($"{elementName} expected = default({elementName}); var actual = {allocationKind} {elementName}[2];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var declarations = Declarations(model);
        var allocation = declarations[1].Initializer;
        Assert.NotNull(allocation);

        var element = Assert.IsType<FunctionType>(ElementOf(allocation));
        Assert.Same(declarations[0].Local.Type, element);
        Assert.Same(element, Assert.IsType<ConstructedType>(allocation.Type).TypeArguments[0]);
    }

    [Theory]
    [InlineData("new func<int, int>[2]", "MIST0225")]
    [InlineData("new Callback[2]", "MIST0225")]
    [InlineData("stackalloc func<int, int>[2]", "MIST0226")]
    [InlineData("stackalloc Callback[2]", "MIST0226")]
    public void ProgramModelToAnalysis_FunctionElementsReachTheReferenceTypeStampabilityGate(string allocationSource, string diagnosticId)
    {
        var model = Build(Source($"var actual = {allocationSource};"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var allocation = Assert.Single(Declarations(model)).Initializer;
        Assert.NotNull(allocation);
        _ = Assert.IsType<FunctionType>(ElementOf(allocation));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var finding = Assert.Single(Errors(findings.ToImmutableArray(), diagnosticId));
        Assert.Contains("reference type", finding.Message, StringComparison.Ordinal);
        Assert.Contains("func<", finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("stackalloc")]
    public void UnresolvedFunctionArgumentsRetainTheirNamedTypeDiagnostic(string allocationKind)
    {
        _ = Build(Source($"var actual = {allocationKind} func<Missing, int>[2];"), out var diagnostics);

        var error = Assert.Single(Errors(diagnostics, "MIST0298"));
        Assert.Contains("Missing", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(error.Position().Start, error.Position().End);
    }

    private static LocalDeclarationStatement[] Declarations(ProgramModelClass model)
    {
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = probe.MethodsList.Single(method => method.Name == "Run").Body;
        Assert.NotNull(body);

        return body.Statements.OfType<LocalDeclarationStatement>().ToArray();
    }

    private static IMistType ElementOf(Expression allocation)
    {
        return allocation switch
        {
            ArrayCreationExpression array => array.ElementType,
            StackallocExpression stack => stack.ElementType,
            _ => throw new InvalidOperationException($"Unexpected allocation kind {allocation.GetType().Name}."),
        };
    }

    private static string Source(string statements)
    {
        return $$"""
            using Standard;

            namespace Standard
            {
                intrinsic public class Func<params TArgs, TResult> { }
            }

            namespace Probe
            {
                type Mapper<T> = func<T, int>;
                type Callback = func<void>;
                public class Probe
                {
                    public static void Run()
                    {
                        {{statements}}
                    }
                }
            }
            """;
    }
}