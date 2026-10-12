// Tier 2 (Functional): the analyzer walks bound fragments and reports at the value each kind of slot holds.

using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class AssignabilitySiteTests
{
    [Theory]
    [InlineData("local-initializer")]
    [InlineData("assignment")]
    [InlineData("coalescing-assignment")]
    [InlineData("return")]
    [InlineData("void-body-return")]
    [InlineData("exit-code-return")]
    [InlineData("argument")]
    [InlineData("constructor-argument")]
    [InlineData("object-initializer")]
    [InlineData("index")]
    [InlineData("projected-argument")]
    [InlineData("projected-constructor-argument")]
    [InlineData("projected-index")]
    [InlineData("coalesce")]
    [InlineData("switch-arm")]
    [InlineData("array-element")]
    [InlineData("lambda-result")]
    [InlineData("lambda-return")]
    [InlineData("foreach-element")]
    [InlineData("deconstruction-element")]
    [InlineData("enum-value")]
    [InlineData("field-initializer")]
    [InlineData("property-initializer")]
    [InlineData("getter-expression")]
    [InlineData("getter-return")]
    [InlineData("indexer-getter-expression")]
    [InlineData("constructor-chain")]
    public void RefusalIsAtTheValueForEveryKindOfSlot(string site)
    {
        var input = new HandBuiltModel();
        var integer = input.Type("Int32");
        var text = input.Type("String");
        var value = HandBuiltModel.Literal(text, "bad", 37);
        var owner = HandBuiltModel.Class("Holder");
        var local = new LocalVariable("target", integer);
        var target = new LocalReferenceExpression(HandBuiltModel.Node(3), local);
        var optional = input.Interner.GetOrCreateUnionType([integer, input.Type("Void")]);
        var array = input.Interner.GetOrCreateConstructedType(input.Type("Array<,>"), [integer, input.Interner.GetOrCreateUnknownValue(integer)]);
        object? root;
        IReadOnlyList<MistType> declarations = [];

        switch(site)
        {
            case "local-initializer":
                root = new LocalDeclarationStatement(local, value);
                break;
            case "assignment":
                root = new AssignmentExpression(HandBuiltModel.Node(3), target, value, integer);
                break;
            case "coalescing-assignment":
                root = new AssignmentExpression(
                    HandBuiltModel.Node(3), new LocalReferenceExpression(HandBuiltModel.Node(3), new LocalVariable("held", optional)), value, integer, isCoalescing: true);
                break;
            case "return":
                root = new ReturnStatement(value, integer);
                break;
            case "void-body-return":
                root = new ReturnStatement(value, input.Type("Void"));
                break;
            case "exit-code-return":
                root = new ReturnStatement(value, integer) { IsExitCode = true };
                break;
            case "argument":
                root = new InvocationExpression(HandBuiltModel.Node(3), target, [value], integer, null, [integer]);
                break;
            case "constructor-argument":
                root = new NewExpression(HandBuiltModel.Node(3), owner, [value], null, [], [integer]);
                break;
            case "object-initializer":
                root = new NewExpression(HandBuiltModel.Node(3), owner, [], null,
                    [new ObjectInitializerAssignment(HandBuiltModel.Node(3), "Count", value, integer)], []);
                break;
            case "index":
                root = new IndexExpression(HandBuiltModel.Node(3), target, value, integer) { ParameterType = integer };
                break;
            case "projected-argument":
                root = new InvocationExpression(HandBuiltModel.Node(3), target, [value], integer, Projected(integer), [integer]);
                break;
            case "projected-constructor-argument":
                root = new NewExpression(HandBuiltModel.Node(3), owner, [value], new Constructor("Holder", owner, [new Parameter("value", integer)], new MemberModifiers { IsClrProjected = true }), [], [integer]);
                break;
            case "projected-index":
                root = new IndexExpression(HandBuiltModel.Node(3), target, value, integer)
                {
                    ParameterType = integer,
                    Indexer = new Indexer(integer, [new Parameter("key", integer)], new MemberModifiers { IsClrProjected = true }),
                };
                break;
            case "coalesce":
                root = new CoalesceExpression(HandBuiltModel.Node(3), HandBuiltModel.Literal(optional), value, integer);
                break;
            case "switch-arm":
                root = new SwitchExpression(HandBuiltModel.Node(3), target,
                    [new SwitchArm(HandBuiltModel.Node(3), new CatchAllPattern(HandBuiltModel.Node(3), integer, null), null, value)], integer);
                break;
            case "array-element":
                root = new ArrayLiteralExpression(HandBuiltModel.Node(3), integer, [value], array);
                break;
            case "lambda-result":
                root = new LambdaExpression(HandBuiltModel.Node(3), [], value, null, false,
                    input.Interner.GetOrCreateFunctionType([], integer));
                break;
            case "lambda-return":
                root = new LambdaExpression(HandBuiltModel.Node(3), [], null,
                    HandBuiltModel.Block(new ReturnStatement(value, integer)), false,
                    input.Interner.GetOrCreateFunctionType([], integer));
                break;
            case "foreach-element":
                root = new ForEachStatement(new LocalVariable("each", text), HandBuiltModel.Literal(array, offset: 37), HandBuiltModel.Block());
                break;
            case "deconstruction-element":
                root = new TupleDeconstructionStatement([new LocalVariable("first", text)],
                    HandBuiltModel.Literal(input.Interner.GetOrCreateTupleType([integer]), offset: 37));
                break;
            case "enum-value":
                var enumeration = HandBuiltModel.Enum("Named", integer);
                enumeration.MembersList.Add(new EnumMember("A", false, value));
                declarations = [enumeration];
                root = null;
                break;
            case "field-initializer":
                owner.FieldsList.Add(new Field("Count", integer, default, default) { Initializer = value });
                declarations = [owner];
                root = null;
                break;
            case "property-initializer":
                owner.PropertiesList.Add(new Property("Count", integer, default) { Initializer = value });
                declarations = [owner];
                root = null;
                break;
            case "getter-expression":
                owner.PropertiesList.Add(new Property("Count", integer, default) { Getter = new PropertyAccessor { ExpressionBody = value } });
                declarations = [owner];
                root = null;
                break;
            case "getter-return":
                root = HandBuiltModel.Block(new ReturnStatement(value, integer));
                owner.PropertiesList.Add(new Property("Count", integer, default) { Getter = new PropertyAccessor { Body = (Block)root } });
                declarations = [owner];
                break;
            case "indexer-getter-expression":
                owner.IndexersList.Add(new Indexer(integer, [], default) { Getter = new PropertyAccessor { ExpressionBody = value } });
                declarations = [owner];
                root = null;
                break;
            case "constructor-chain":
                var constructor = new Constructor("Holder", owner, [], default) { ChainParameterTypes = [integer] };
                constructor.ChainArgumentsList.Add(value);
                owner.ConstructorsList.Add(constructor);
                declarations = [owner];
                root = null;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }

        var diagnostic = Assert.Single(input.Analyze(new AssignabilityAnalyzer(), root, declarations));

        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(1, diagnostic.Location.Start.Line);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void AnAmbiguousConversionReportsAtItsOriginalOperand()
    {
        var input = new AssignabilityTypes();
        var value = HandBuiltModel.Literal(input["Tally"], offset: 37);
        var tally = Assert.IsType<ClassType>(input["Tally"]);
        var converted = new ImplicitConversionExpression(value.Node, value, input["Double"],
            [.. tally.CastOperatorsList.Select(conversion => new ConversionFunction(conversion, tally, conversion.ReturnType))]);

        var diagnostic = Assert.Single(input.Model.Analyze(new AssignabilityAnalyzer(), converted));

        Assert.Equal("MIST_PA_AMBIGUOUS_IMPLICIT_CONVERSION", diagnostic.Id.Value);
        Assert.Equal(38, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData("unpaired-call")]
    [InlineData("elementless-params-call")]
    [InlineData("from-end-index")]
    [InlineData("failed-target")]
    public void ASlotTheAnalyzerDoesNotOwnRemainsSilent(string site)
    {
        var input = new HandBuiltModel();
        var integer = input.Type("Int32");
        var value = HandBuiltModel.Literal(input.Type("String"), "bad");
        var receiver = HandBuiltModel.Literal(integer);
        var parameter = new Parameter("value", integer) { IsFlattened = site == "elementless-params-call" };
        var method = new Method("Take", integer, [parameter], default);
        object root = site switch
        {
            "elementless-params-call" => new InvocationExpression(HandBuiltModel.Node(), receiver, [value], integer, method, [integer]),
            "unpaired-call" => new InvocationExpression(HandBuiltModel.Node(), receiver, [value], integer, method, []),
            "from-end-index" => new IndexExpression(HandBuiltModel.Node(), receiver, value, integer),
            "failed-target" => new AssignmentExpression(HandBuiltModel.Node(), new ErrorExpression(HandBuiltModel.Node()), value, integer),
            _ => throw new ArgumentOutOfRangeException(nameof(site)),
        };

        Assert.Empty(input.Analyze(new AssignabilityAnalyzer(), root));
    }

    [Fact]
    public void ASubstitutedRefinementIsDisplayedWithItsBoundBase()
    {
        var input = new AssignabilityTypes();
        var root = new LocalDeclarationStatement(new LocalVariable("named", input["Ne<String>"]), HandBuiltModel.Literal(input["Ne<Int32>"]));

        var diagnostic = Assert.Single(input.Model.Analyze(new AssignabilityAnalyzer(), root));

        Assert.Contains("'int' to 'string'", diagnostic.Message);
    }

    private static Method Projected(ClassType parameterType)
    {
        return new Method("Take", parameterType, [new Parameter("value", parameterType)], new MemberModifiers { IsClrProjected = true });
    }
}