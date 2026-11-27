using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltRefinements;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.ProgramAnalysis;

public sealed class SpanLengthDecisionTests
{
    [Theory]
    [Trait("Tier", "4")]
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
    [Trait("Tier", "4")]
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
    [Trait("Tier", "2")]
    [InlineData("Span", "lower and upper", null)]
    [InlineData("ReadOnlySpan", "lower and upper", null)]
    [InlineData("Span", "reversed comparisons", null)]
    [InlineData("ReadOnlySpan", "negated comparisons", null)]
    [InlineData("Span", "upper only", "MIST0275")]
    [InlineData("ReadOnlySpan", "lower only", "MIST0275")]
    [InlineData("Span", "inclusive upper", "MIST0275")]
    public void AnIndexGuardMustEstablishBothBounds(string type, string form, string? expected)
    {
        var span = new SpanAccess(type);
        var h = span.Values;
        var zero = h.Literal(0L);
        var guard = form switch
        {
            "lower and upper" => span.InBounds(),
            "reversed comparisons" => h.And(h.Binary(zero, BinaryOperatorKind.LessThanOrEqual, span.Index),
                h.Binary(span.Length, BinaryOperatorKind.GreaterThan, span.Index)),
            "negated comparisons" => h.And(h.Not(h.Binary(span.Index, BinaryOperatorKind.LessThan, zero)),
                h.Not(h.Binary(span.Index, BinaryOperatorKind.GreaterThanOrEqual, span.Length))),
            "upper only" => h.Binary(span.Index, BinaryOperatorKind.LessThan, span.Length),
            "lower only" => h.Binary(span.Index, BinaryOperatorKind.GreaterThanOrEqual, zero),
            "inclusive upper" => h.And(h.Binary(span.Index, BinaryOperatorKind.GreaterThanOrEqual, zero),
                h.Binary(span.Index, BinaryOperatorKind.LessThanOrEqual, span.Length)),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        span.Check(expected, If(guard, span.Return(span.Element(span.Index))), span.Return(zero));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Span", "nonempty", null)]
    [InlineData("ReadOnlySpan", "nonempty reversed", null)]
    [InlineData("Span", "empty returns early", null)]
    [InlineData("ReadOnlySpan", "at least two", null)]
    [InlineData("ReadOnlySpan", "not one", "MIST0275")]
    [InlineData("Span", "positive", "MIST0275")]
    public void ALengthGuardMustProveTheChosenElementExists(string type, string form, string? expected)
    {
        var span = new SpanAccess(type);
        var h = span.Values;
        var zero = h.Literal(0L);
        var one = h.Literal(1L);
        if(form == "empty returns early")
        {
            span.Check(expected, If(h.Binary(span.Length, BinaryOperatorKind.Equal, zero), span.Return(zero)),
                span.Return(span.Element(zero)));
            return;
        }

        var (guard, element) = form switch
        {
            "nonempty" => (h.Binary(span.Length, BinaryOperatorKind.NotEqual, zero), span.Element(zero)),
            "nonempty reversed" => (h.Binary(zero, BinaryOperatorKind.NotEqual, span.Length), span.FromEnd(one)),
            "at least two" => (h.Binary(span.Length, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(2L)), span.Element(one)),
            "not one" => (h.Binary(span.Length, BinaryOperatorKind.NotEqual, one), span.Element(zero)),
            "positive" => (h.Binary(span.Length, BinaryOperatorKind.GreaterThan, zero), span.Element(one)),
            _ => throw new ArgumentOutOfRangeException(nameof(form)),
        };

        span.Check(expected, If(guard, span.Return(element)), span.Return(zero));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Span", "element", null)]
    [InlineData("ReadOnlySpan", "index", "MIST0274")]
    [InlineData("Span", "receiver", "MIST0275")]
    [InlineData("ReadOnlySpan", "nonempty receiver", "MIST0275")]
    public void AWrittenElementPreservesTheProofButAWrittenIndexOrReceiverInvalidatesIt(string type, string target, string? expected)
    {
        var span = new SpanAccess(type);
        var h = span.Values;
        var zero = h.Literal(0L);
        var guard = target == "nonempty receiver" ? h.Binary(span.Length, BinaryOperatorKind.GreaterThan, zero) : span.InBounds();
        var write = target switch
        {
            "element" => Assign(span.Element(span.Index), h.Literal(4L)),
            "index" => Assign(span.Index, new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, h.Literal(1L), h.Int)),
            "receiver" or "nonempty receiver" => Assign(span.Receiver, SpanAccess.Parameter(span.Receiver.Type, "other")),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        var read = span.Element(target == "nonempty receiver" ? zero : span.Index);

        span.Check(expected, If(guard, Evaluate(write), span.Return(read)), span.Return(zero));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Span")]
    [InlineData("ReadOnlySpan")]
    public void ALoopFromZeroToTheErasedLengthProvesEveryRead(string type)
    {
        var span = new SpanAccess(type);
        var h = span.Values;
        var j = Local(h.Int, "j");
        var total = Local(h.Int, "total");
        var loop = new ForStatement(Give(j, h.Literal(0L)), h.Binary(j, BinaryOperatorKind.LessThan, span.Length),
            Assign(j, h.Binary(j, BinaryOperatorKind.Add, h.Literal(1L), h.Int)),
            HandBuiltModel.Block(Evaluate(Assign(total, h.Binary(total, BinaryOperatorKind.Add, span.Element(j), h.Int)))));

        span.Check(null, Give(total, h.Literal(0L)), loop, span.Return(total));
    }

    [Theory]
    [Trait("Tier", "4")]
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
    [Trait("Tier", "4")]
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
    [Trait("Tier", "4")]
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

    private sealed class SpanAccess
    {
        public HandBuiltRefinements Values { get; } = new();
        private readonly LocalVariable receiver;
        private readonly LocalVariable index;
        private readonly TypeParameter lengthParameter;
        private readonly UnknownValueType erasedLength;
        private readonly RefinedType bounds;

        public SpanAccess(string type)
        {
            var h = Values;
            var definition = new StructType(type + "<,>", "Standard", new Scope(null), null, null)
            { Modifiers = new MemberModifiers { IsIntrinsic = true, IsRef = true } };
            var nonnegative = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt");
            lengthParameter = new TypeParameter("Length", 1, definition.Scope, definition) { IsDependent = true, Domain = nonnegative };
            definition.TypeParametersList.Add(new TypeParameter("T", 0, definition.Scope, definition));
            definition.TypeParametersList.Add(lengthParameter);
            erasedLength = new UnknownValueType(nonnegative);
            bounds = h.Refine(nonnegative, ComparisonKind.LessThan, new TypeReferenceExpression(HandBuiltModel.Node(), lengthParameter), "SpanBounds");
            var sequence = h.Built.Interner.GetOrCreateConstructedType(definition, [h.Int, UnknownType.Instance]);
            receiver = Parameter(sequence, "span").Local;
            index = Parameter(h.Int, "i").Local;
        }

        public LocalReferenceExpression Receiver => Read(receiver);
        public LocalReferenceExpression Index => Read(index);
        public MemberAccessExpression Length => new(HandBuiltModel.Node(), Receiver, "Length", erasedLength, member: new TypeResult(lengthParameter));

        public static LocalReferenceExpression Parameter(IMistType type, string name)
        {
            return Read(new LocalVariable(name, type) { Parameter = new Parameter(name, type) });
        }

        public IndexExpression Element(Expression subscript)
        {
            return new IndexExpression(HandBuiltModel.Node(), Receiver, subscript, Values.Int) { ParameterType = bounds };
        }

        public IndexExpression FromEnd(Expression distance)
        {
            return new IndexExpression(HandBuiltModel.Node(), Receiver, Values.Back(distance), Values.Int);
        }

        public Expression InBounds()
        {
            return Values.And(Values.Binary(Index, BinaryOperatorKind.GreaterThanOrEqual, Values.Literal(0L)),
                Values.Binary(Index, BinaryOperatorKind.LessThan, Length));
        }

        public ReturnStatement Return(Expression value)
        {
            return new ReturnStatement(value, Values.Int);
        }

        public void Check(string? expected, params Statement[] statements)
        {
            var findings = Values.Built.Analyze(new RefinementAnalyzer(), HandBuiltModel.Block(statements));
            if(expected is null)
            {
                Assert.Empty(findings);
            }
            else
            {
                Assert.Equal(expected, Assert.Single(findings).Number.Value);
            }
        }
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