// Tier 3: erased dependent lengths retain their runtime domain and reach the existing index prover.

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class SpanLengthDecisionTests
{
    [Theory]
    [InlineData("Span<int>")]
    [InlineData("ReadOnlySpan<int>")]
    [InlineData("Span<int, unknown>")]
    [InlineData("ReadOnlySpan<int, unknown>")]
    [InlineData("Array<int, unknown>")]
    [InlineData("int[]")]
    public void AnErasedLength_HasTheDeclaredRuntimeDomainWithoutAKnownValue(string type)
    {
        var model = Build($"public static int Read({type} span) {{ int n = span.Length; return n; }}", out var nodes);
        var length = Assert.Single(nodes.OfType<MemberAccessExpression>(), access => access.MemberName == "Length");
        var parameter = Assert.IsType<TypeParameter>(Assert.IsType<TypeResult>(length.Member).Type);
        var unknown = Assert.IsType<UnknownValueType>(length.Type);

        Assert.Equal(
            ConversionRelation.Unrefined(parameter.Domain!).FullyQualifiedName,
            ConversionRelation.Unrefined(unknown.Domain).FullyQualifiedName);
        Assert.Equal("Standard.Int32", ConversionRelation.Unrefined(unknown.Domain).FullyQualifiedName);

        if(Assert.IsType<ConstructedType>(length.Receiver.Type).TypeArguments[1] is UnknownType)
        {
            Assert.Same(parameter.Domain, unknown.Domain);
            Assert.Equal("ZeroOrPositiveInt", unknown.Domain.Name);
            var requirement = Assert.IsType<Requirement>(Requirement.OfType(unknown.Domain));
            Assert.True(requirement.Values.Contains(0));
            Assert.False(requirement.Values.Contains(-1));
        }

        Assert.Null(Requirement.Constant(length));
        Assert.Null(Lengths.Typed(length.Receiver.Type));
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("Span<int, 3>")]
    [InlineData("ReadOnlySpan<int, 3>")]
    [InlineData("Array<int, 3>")]
    public void AKnownLength_KeepsItsLiftedValueAndIndexSpecialization(string type)
    {
        var model = Build($"public static int Read({type} span) {{ var n = span.Length; return span[2]; }}", out var nodes);
        var length = Assert.Single(nodes.OfType<MemberAccessExpression>(), access => access.MemberName == "Length");
        var lifted = Assert.IsType<LiftedType>(length.Type);
        var element = Assert.Single(nodes.OfType<IndexExpression>());

        Assert.Equal(3L, lifted.Value);
        Assert.Equal("Standard.Int32", lifted.Type.FullyQualifiedName);
        Assert.Equal(3L, Lengths.Exact(Lengths.Typed(length.Receiver.Type)));
        Assert.NotNull(element.ParameterType);
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("Span<int>", "if(i >= 0 && i < span.Length) { return span[i]; } return 0;")]
    [InlineData("ReadOnlySpan<int>", "if(i >= 0 && i < span.Length) { return span[i]; } return 0;")]
    [InlineData("Span<int>", "if(0 <= i && span.Length > i) { return span[i]; } return 0;")]
    [InlineData("ReadOnlySpan<int>", "if(!(i < 0) && !(i >= span.Length)) { return span[i]; } return 0;")]
    [InlineData("Span<int>", "if(span.Length != 0) { return span[0]; } return 0;")]
    [InlineData("ReadOnlySpan<int>", "if(0 != span.Length) { return span[^1]; } return 0;")]
    [InlineData("Span<int>", "if(span.Length == 0) { return 0; } return span[0];")]
    [InlineData("ReadOnlySpan<int>", "if(span.Length >= 2) { return span[1]; } return 0;")]
    [InlineData("Span<int>", "if(i >= 0 && i < span.Length) { span[i] = 4; return span[i]; } return 0;")]
    [InlineData("Span<int>", "int total = 0; for(int j = 0; j < span.Length; j = j + 1) { total = total + span[j]; } return total;")]
    [InlineData("ReadOnlySpan<int>", "int total = 0; for(int j = 0; j < span.Length; j = j + 1) { total = total + span[j]; } return total;")]
    public void AnErasedSpanLengthGuard_ProvesTheAccess(string type, string body)
    {
        var model = Build($"public static int Read({type} span, int i) {{ {body} }}", out var nodes);

        Assert.Contains(nodes, node => node is MemberAccessExpression { MemberName: "Length", Type: UnknownValueType });
        Assert.NotEmpty(nodes.OfType<IndexExpression>());
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("Span<int>", "if(i < span.Length) { return span[i]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("ReadOnlySpan<int>", "if(i >= 0) { return span[i]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Span<int>", "if(i >= 0 && i <= span.Length) { return span[i]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("ReadOnlySpan<int>", "if(span.Length != 1) { return span[0]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("Span<int>", "if(span.Length > 0) { return span[1]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("ReadOnlySpan<int>", "if(i >= 0 && i < span.Length) { i = -1; return span[i]; } return 0;", "MIST_PA_INDEX_OUT_OF_BOUNDS")]
    [InlineData("Span<int>", "if(i >= 0 && i < span.Length) { span = other; return span[i]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    [InlineData("ReadOnlySpan<int>", "if(span.Length > 0) { span = other; return span[0]; } return 0;", "MIST_PA_INDEX_UNPROVEN")]
    public void AnInsufficientGuardOrAWrite_LeavesTheAccessRefused(string type, string body, string id)
    {
        var model = Build($"public static int Read({type} span, {type} other, int i) {{ {body} }}", out var nodes);

        Assert.NotEmpty(nodes.OfType<IndexExpression>());
        Assert.Equal(id, Assert.Single(Findings(model)).Number.Value);
    }

    [Theory]
    [InlineData("Span<int>")]
    [InlineData("ReadOnlySpan<int>")]
    public void ASliceCall_ResolvesItsDeclaredSlotsAndRetainsItsErasedLength(string type)
    {
        var model = Build($"public static int Read({type} span) {{ if(span.Length >= 2) {{ var slice = span.Slice(0, 2); return slice[1]; }} return 0; }}", out var nodes);
        var slice = Assert.Single(nodes.OfType<InvocationExpression>());
        var result = Assert.IsType<ConstructedType>(slice.Type);

        Assert.Equal("Slice", slice.Method?.Name);
        Assert.NotNull(slice.ParameterTypes);
        Assert.Equal(2, slice.ParameterTypes.Count);
        Assert.NotNull(slice.ParameterTypes[0]);
        Assert.Equal("Standard.Int32", slice.ParameterTypes[1].FullyQualifiedName);
        Assert.IsNotType<LiftedType>(result.TypeArguments[1]);
        Assert.Null(Lengths.Typed(result));
        Assert.Empty(Findings(model));
    }

    [Theory]
    [InlineData("int", "Standard.Int32")]
    [InlineData("bool", "Standard.Boolean")]
    [InlineData("char", "Standard.Char")]
    public void AnErasedUserDependentMember_RecoversItsOwnDomain(string domain, string expected)
    {
        var model = Build(
            $"public static {domain} Read(Meter<unknown> meter) {{ return meter.Value; }}",
            out var nodes,
            $"public class Meter<N:{domain}> {{ public N Value {{ get; set; }} }}");
        var value = Assert.Single(nodes.OfType<MemberAccessExpression>());
        var unknown = Assert.IsType<UnknownValueType>(value.Type);

        Assert.Equal(expected, ConversionClassifier.Normalized(unknown.Domain, model.TypeInterner).FullyQualifiedName);
        Assert.Null(Requirement.Constant(value));
        Assert.Empty(Findings(model));
    }

    [Fact]
    public void AnErasedOrdinaryTypeParameter_RemainsTheTopType()
    {
        var model = Build(
            "public static unknown Read(Box<unknown> box) { return box.Value; }",
            out var nodes,
            "public class Box<T> { public T Value { get; set; } }");
        var value = Assert.Single(nodes.OfType<MemberAccessExpression>());

        Assert.Same(UnknownType.Instance, value.Type);
        Assert.Empty(Findings(model));
    }

    private static ProgramModelClass Build(string members, out List<object> nodes, string extraTypes = "")
    {
        var model = StandardModelProbe.Build("", out var diagnostics, $"{extraTypes} public class Spans {{ {members} }}");
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var declaring = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Spans"]);
        nodes = [.. declaring.MethodsList.SelectMany(method => BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(method.Body)))];
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        return model;
    }

    private static ImmutableArray<Diagnostic> Findings(ProgramModelClass model)
    {
        var diagnostics = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, diagnostics);
        return diagnostics.ToImmutableArray();
    }
}