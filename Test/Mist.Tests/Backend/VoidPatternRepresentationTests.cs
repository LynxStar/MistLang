using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class VoidPatternRepresentationTests
{
    private readonly HandBuiltModel model = new();
    private readonly StructType unit = new("Void", "Standard", new Scope(null), null, null);

    [Theory]
    [InlineData(false, "(value is var __e1 && true)")]
    [InlineData(true, "(value is var __e1 && !(true))")]
    public void AConcreteUnitTest_CapturesTheOperandAndUsesABooleanGuard(bool negated, string expected)
    {
        Assert.Equal(expected, Emit(Is(Local(unit), Pattern(unit, negated))));
    }

    [Theory]
    [InlineData("optional", false, "value is null")]
    [InlineData("optional", true, "value is not null")]
    [InlineData("unknown", false, "value is (null or Standard.Void)")]
    [InlineData("unknown", true, "value is not (null or Standard.Void)")]
    [InlineData("carrier", false, "value.AsVoidOrNull is Void")]
    public void NullableAndCarrierInputs_KeepTheirRepresentationTests(string representation, bool negated, string expected)
    {
        IMistType input = representation switch
        {
            "optional" => new UnionType([model.Type("Int32"), unit]),
            "carrier" => new UnionType([model.Type("Int32"), model.Type("String"), unit]),
            _ => UnknownType.Instance,
        };
        Assert.Equal(expected, Emit(Is(Local(input), Pattern(input, negated))));
    }

    [Fact]
    public void AUnitTest_UsesTheClosedAndErasedInputType()
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var alias = new TypeAlias("Unit", "Probe", new Scope(null), null, new TypeAliasDeclarationNode()) { AliasedType = unit };
        var refined = HandBuiltModel.Refined(unit);

        foreach(var input in new IMistType[] { alias, refined })
        {
            Assert.Equal("(value is var __e1 && true)", Emit(Is(Local(input), Pattern(input))));
        }

        Assert.Equal("value is (null or Standard.Void)", Emit(Is(Local(parameter), Pattern(parameter))));
        using var substitution = TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [unit]));
        Assert.Equal("(value is var __e1 && true)", Emit(Is(Local(parameter), Pattern(parameter))));
    }

    [Fact]
    public void AUnitBinding_RemainsADeclarationPattern()
    {
        var pattern = new TypeTestPattern(null!, unit, new LocalVariable("held", unit)) { ScrutineeType = unit };
        Assert.Equal("value is Void held", Emit(Is(Local(unit), pattern)));
    }

    [Theory]
    [InlineData("is")]
    [InlineData("not")]
    [InlineData("switch")]
    [InlineData("and")]
    [InlineData("or")]
    public void ACallOperand_AppearsExactlyOnceAcrossPatternForms(string form)
    {
        var function = new FunctionType([], unit);
        var call = new InvocationExpression(null!, Local(function, "Next"), [], unit, null, []);
        Pattern pattern = form switch
        {
            "not" => Pattern(unit, negated: true),
            "and" => new ConjunctionPattern(null!, unit, [Pattern(unit), new CatchAllPattern(null!, unit, null)]),
            "or" => new DisjunctionPattern(null!, unit, [Pattern(unit), new CatchAllPattern(null!, unit, null)]),
            _ => Pattern(unit),
        };
        var emitted = Emit(form == "switch" ? Switch(call, pattern) : Is(call, pattern));

        Assert.Equal(1, emitted.Split("Next()", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("null", emitted, StringComparison.Ordinal);
        Assert.Contains("true", emitted, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedUnitTests_KeepTheirGuardsOutsideStructuralPatterns(bool negated)
    {
        var holder = HandBuiltModel.Class("Holder");
        var property = new PropertyPattern(null!, holder,
            [new PropertySubpattern(null!, "Value", Pattern(unit, negated), unit, new NotFoundResult())]);
        var tupleType = new TupleType([unit, model.Type("Int32")]);
        var tuple = new TuplePattern(null!, [Pattern(unit, negated), new CatchAllPattern(null!, model.Type("Int32"), null)], tupleType, null, null);
        var array = new ConstructedType(model.Type("Array"), [unit]);
        var list = new ListPattern(null!, [new ListPatternElement(Pattern(unit, negated), false, unit)], array);

        foreach(var (input, pattern) in new (IMistType, Pattern)[] { (holder, property), (tupleType, tuple), (array, list) })
        {
            var emitted = Emit(Is(Local(input), pattern));
            Assert.Contains("var __e1", emitted, StringComparison.Ordinal);
            Assert.Contains(negated ? "&& !(true)" : "&& true", emitted, StringComparison.Ordinal);
            Assert.DoesNotContain("null", emitted, StringComparison.Ordinal);

            var switched = Emit(Switch(Local(input), pattern));
            Assert.Contains(negated ? "when !(true)" : "when true", switched, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AUnitPropertyPrefix_DoesNotProduceTwoPropertyClauses()
    {
        var property = new PropertyPattern(null!, unit, [], typeTest: (TypeTestPattern)Pattern(unit));
        Assert.Equal("value is { }", Emit(Is(Local(unit), property)));
        Assert.Equal("(value is var __e1 && !((__e1 is { })))",
            Emit(Is(Local(unit), new NegatedPattern(null!, unit, property))));
    }

    private Pattern Pattern(IMistType input, bool negated = false)
    {
        var tested = new TypeTestPattern(null!, unit, null, input is UnionType { Constituents.Count: > 2 } ? unit : null)
        {
            ScrutineeType = input,
            TestsAbsence = input is UnionType { Constituents.Count: 2 },
        };
        return negated ? new NegatedPattern(null!, input, tested) : tested;
    }

    private IsPatternExpression Is(Expression value, Pattern pattern)
    {
        return new IsPatternExpression(null!, value, pattern, model.Type("Boolean"));
    }

    private SwitchExpression Switch(Expression value, Pattern pattern)
    {
        var integer = model.Type("Int32");
        return new SwitchExpression(null!, value, [new SwitchArm(null!, pattern, null, HandBuiltModel.Literal(integer, 1))], integer);
    }

    private static LocalReferenceExpression Local(IMistType type, string name = "value")
    {
        return new LocalReferenceExpression(null!, new LocalVariable(name, type));
    }

    private static string Emit(Expression expression)
    {
        BodyEmitter.ResetTemporaries();
        var diagnostics = new DiagnosticBag();
        var emitted = BodyEmitter.Render(expression, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());
        return emitted;
    }
}