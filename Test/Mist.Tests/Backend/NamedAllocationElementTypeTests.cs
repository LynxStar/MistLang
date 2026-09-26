using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

public sealed class NamedAllocationElementTypeTests : ProgramModelProbe
{
    public NamedAllocationElementTypeTests()
        : base("MistNamedAllocation_")
    {
    }

    protected override string Fixtures { get; } = "";

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("new", "Pair<int>")]
    [InlineData("new", "Ns.Point")]
    [InlineData("new", "Ns.Pair<int>")]
    [InlineData("new", "PointAlias")]
    [InlineData("new", "PairAlias<int>")]
    [InlineData("new", "IntPairAlias")]
    [InlineData("new", "PairAliasChain<int>")]
    [InlineData("new", "Ns.IntPair")]
    [InlineData("stackalloc", "Pair<int>")]
    [InlineData("stackalloc", "Ns.Point")]
    [InlineData("stackalloc", "Ns.Pair<int>")]
    [InlineData("stackalloc", "PointAlias")]
    [InlineData("stackalloc", "PairAlias<int>")]
    [InlineData("stackalloc", "IntPairAlias")]
    [InlineData("stackalloc", "PairAliasChain<int>")]
    [InlineData("stackalloc", "Ns.IntPair")]
    public void NamedElementsShareTheOrdinaryTypeReferencesInternedIdentity(string allocationKind, string elementName)
    {
        var model = Build(Source($"{elementName} expected = default({elementName}); var actual = {allocationKind} {elementName}[2];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var declarations = Declarations(model);
        var allocation = declarations[1].Initializer;
        Assert.NotNull(allocation);

        var elementType = ElementOf(allocation);

        Assert.Same(declarations[0].Local.Type, elementType);
        Assert.Same(elementType, Assert.IsType<ConstructedType>(allocation.Type).TypeArguments[0]);

        if(elementType is ConstructedType constructed)
        {
            Assert.Same(model.TypesByFQN[elementName == "Pair<int>" ? "Probe.Pair<>" : "Ns.Pair<>"], constructed.Definition);
            Assert.Same(model.TypesByFQN["Standard.Int32"], Assert.Single(constructed.TypeArguments));
        }
        else
        {
            Assert.Same(model.TypesByFQN["Ns.Point"], elementType);
        }
    }

    [Theory]
    [InlineData("new Pair[2]", "Pair")]
    [InlineData("new Pair<int, int>[2]", "Pair<,>")]
    [InlineData("new Ns.Pair<int, int>[2]", "Ns.Pair<,>")]
    [InlineData("new Ns.Point<int>[2]", "Ns.Point<>")]
    [InlineData("new PairAlias<int, int>[2]", "PairAlias<,>")]
    [InlineData("new Ns.Missing<int>[2]", "Ns.Missing<>")]
    [InlineData("new Missing[2]", "Missing")]
    [InlineData("stackalloc Pair[2]", "Pair")]
    [InlineData("stackalloc Pair<int, int>[2]", "Pair<,>")]
    [InlineData("stackalloc Ns.Pair<int, int>[2]", "Ns.Pair<,>")]
    [InlineData("stackalloc Ns.Point<int>[2]", "Ns.Point<>")]
    [InlineData("stackalloc PairAlias<int, int>[2]", "PairAlias<,>")]
    [InlineData("stackalloc Ns.Missing<int>[2]", "Ns.Missing<>")]
    [InlineData("stackalloc Missing[2]", "Missing")]
    public void InvalidNamedElementsReportTheRequestedNameAndArity(string allocation, string requestedName)
    {
        var model = Build(Source($"var actual = {allocation};"), out var diagnostics);

        var error = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
        Assert.Contains(requestedName, error.Message, StringComparison.Ordinal);
        Assert.NotEqual(error.Location.Start, error.Location.End);
        _ = Assert.IsType<ErrorType>(ElementOf(Initializer(model)));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("stackalloc")]
    public void UnresolvedTypeArgumentsRetainTheirNamedTypeDiagnostic(string allocationKind)
    {
        var model = Build(Source($"var actual = {allocationKind} Ns.Pair<Missing>[2];"), out var diagnostics);

        var error = Assert.Single(Errors(diagnostics, "MIST_PM_UNRESOLVED_TYPE"));
        Assert.Contains("Missing", error.Message, StringComparison.Ordinal);
        Assert.NotEqual(error.Location.Start, error.Location.End);

        var element = Assert.IsType<ConstructedType>(ElementOf(Initializer(model)));
        Assert.Same(model.TypesByFQN["Ns.Pair<>"], element.Definition);
        _ = Assert.IsType<ErrorType>(Assert.Single(element.TypeArguments));
    }

    [Theory]
    [InlineData("new")]
    [InlineData("stackalloc")]
    public void GenericElementsRecordTypeArgumentsAndConstructorConstraintClosures(string allocationKind)
    {
        var model = Build(Source($"var actual = {allocationKind} Ns.ConstructorCarrier<Ns.Point>[2];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var element = Assert.IsType<ConstructedType>(ElementOf(Initializer(model)));
        Assert.Same(model.TypesByFQN["Ns.ConstructorCarrier<>"], element.Definition);
        Assert.Same(model.TypesByFQN["Ns.Point"], Assert.Single(element.TypeArguments));

        var closing = Assert.Single(model.ConstructorConstraintClosures.Closings,
            closing => closing.Parameter.DeclaringType == element.Definition);
        Assert.Same(element.TypeArguments[0], closing.Argument);
        _ = Assert.IsType<NamedElementTypeRefElementTypeRefNode>(closing.Node);

        var closure = Assert.Single(model.ConstructorConstraintClosures.Closures);
        Assert.Same(element.TypeArguments[0], closure.TypeArgument);
        Assert.Same(model.TypesByFQN["Standard.Int32"], Assert.Single(closure.Signature));
        Assert.Same(closing.Node, closure.Node);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("stackalloc")]
    public void AComputedSizeRemainsAnInvocationWithAnUnknownLength(string allocationKind)
    {
        var model = Build(Source($"var actual = {allocationKind} PairAlias<int>[Size()];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var allocation = Initializer(model);
        var size = allocation switch
        {
            ArrayCreationExpression array => array.Size,
            StackallocExpression stack => stack.Size,
            _ => throw new InvalidOperationException($"Unexpected allocation kind {allocation.GetType().Name}."),
        };

        var invocation = Assert.IsType<InvocationExpression>(size);
        Assert.NotNull(invocation.Method);
        Assert.Equal("Size", invocation.Method.Name);
        Assert.Empty(invocation.Arguments);
        Assert.Same(model.TypesByFQN["Standard.Int32"], invocation.Type);
        var length = Assert.IsType<UnknownValueType>(Assert.IsType<ConstructedType>(allocation.Type).TypeArguments[1]);
        Assert.Same(model.TypesByFQN["Standard.Int32"], length.Domain);
    }

    [Theory]
    [InlineData("new Ns.Owner[2]", "MIST_PA_ARRAY_REQUIRES_INITIALIZER", "reference type")]
    [InlineData("new OwnerAlias[2]", "MIST_PA_ARRAY_REQUIRES_INITIALIZER", "reference type")]
    [InlineData("new Ns.NonStampable<int>[2]", "MIST_PA_ARRAY_REQUIRES_INITIALIZER", "field 'value'")]
    [InlineData("new Ns.RefPoint[2]", "MIST_PA_ARRAY_REQUIRES_INITIALIZER", "ref struct")]
    [InlineData("stackalloc Ns.Owner[2]", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE", "reference type")]
    [InlineData("stackalloc OwnerAlias[2]", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE", "reference type")]
    [InlineData("stackalloc Ns.NonStampable<int>[2]", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE", "field 'value'")]
    [InlineData("stackalloc Ns.RefPoint[2]", "MIST_PA_STACKALLOC_ELEMENT_NOT_STAMPABLE", "ref struct")]
    public void ProgramModelToAnalysis_ResolvedElementsReachTheStampabilityGate(string allocation, string diagnosticId, string reason)
    {
        var model = Build(Source($"var actual = {allocation};"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.IsNotType<ErrorType>(ElementOf(Initializer(model)));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var finding = Assert.Single(Errors(findings.ToImmutableArray(), diagnosticId));
        Assert.Contains(reason, finding.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("new")]
    [InlineData("stackalloc")]
    public void ProgramModelToAnalysis_RefStructArgumentsReachTheExistingTypeArgumentCheck(string allocationKind)
    {
        var model = Build(Source($"var actual = {allocationKind} Ns.Pair<Ns.RefPoint>[2];"), out var diagnostics);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        var finding = Assert.Single(Errors(findings.ToImmutableArray(), "MIST_PA_REF_STRUCT_TYPE_ARGUMENT"));
        Assert.Contains("RefPoint", finding.Message, StringComparison.Ordinal);
    }

    private static LocalDeclarationStatement[] Declarations(ProgramModelClass model)
    {
        var probe = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]);
        var body = probe.MethodsList.Single(method => method.Name == "Run").Body;
        Assert.NotNull(body);

        return body.Statements.OfType<LocalDeclarationStatement>().ToArray();
    }

    private static Expression Initializer(ProgramModelClass model)
    {
        var initializer = Assert.Single(Declarations(model)).Initializer;
        Assert.NotNull(initializer);

        return initializer;
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
            namespace Ns
            {
                public struct Point
                {
                    public int x;
                    public Point(int value) { x = value; }
                }

                public struct Pair<T> { public int first; public int second; }
                public struct ConstructorCarrier<T> where T : new(int) { public int count; }
                public struct NonStampable<T> { public T value; }
                public ref struct RefPoint { public int x; }
                public class Owner { public int x; }
                public type IntPair = Pair<int>;
            }

            namespace Probe
            {
                public struct Pair<T> { public int first; public int second; }
                type PointAlias = Ns.Point;
                type PairAlias<T> = Ns.Pair<T>;
                type IntPairAlias = Ns.Pair<int>;
                type PairAliasChain<T> = PairAlias<T>;
                type OwnerAlias = Ns.Owner;

                public class Probe
                {
                    public static void Run()
                    {
                        {{statements}}
                    }

                    public static int Size() { return 2; }
                }
            }
            """;
    }
}