using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;
using static Mist.Tests.Helpers.HandBuiltRefinements;
using static Mist.Tests.ProgramAnalysis.RealFlowBody;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealFlowDecisionTests
{
    [Theory]
    [Trait("Tier", "2")]
    [InlineData("Unit", "NonNegative", false)]
    [InlineData("PositiveUnit", "Unit", false)]
    [InlineData("PositiveUnit", "Positive", false)]
    [InlineData("Outside", "NotZero", true)]
    [InlineData("StrictUnit", "Unit", true)]
    [InlineData("DecimalUnit", "DecimalNonNegative", false)]
    [InlineData("FloatUnit", "Unit", false)]
    public void ADeclaredRealSubset_ProvesAWeakerRefinement(string source, string target, bool nested)
    {
        var r = new RealFlowBody();
        var parameter = new Parameter("first", r.Type(source));
        var value = new LocalVariable(parameter.Name, parameter.Type) { Parameter = parameter };
        var assignment = Need(Read(value), r.Type(target));
        if(nested)
        {
            var function = new Method("Forward", r.Values.Built.Type("Void"), [parameter], new MemberModifiers()) { Body = Block(assignment) };
            r.Check(null, new LocalFunctionStatement(function, [value]));
        }
        else
        {
            r.Check(null, assignment);
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("double", "> 0", false, "Positive", null)]
    [InlineData("double", "0 <", false, "Positive", null)]
    [InlineData("double", "double negation", false, "Positive", null)]
    [InlineData("double", "between", false, "Unit", null)]
    [InlineData("double", "between reversed", false, "Unit", null)]
    [InlineData("double", "negated between", true, "Unit", null)]
    [InlineData("double", "outside", false, "Outside", null)]
    [InlineData("double", "outside reversed", false, "Outside", null)]
    [InlineData("double", "== 0", true, "NotZero", null)]
    [InlineData("double", "0 ==", true, "NotZero", null)]
    [InlineData("double", "!= 0", false, "NotZero", null)]
    [InlineData("double", "not positive", false, "NotPositive", null)]
    [InlineData("double", "> 0", true, "NotPositive", null)]
    [InlineData("float", "between", false, "Unit", null)]
    [InlineData("float", "between reversed", false, "FloatUnit", null)]
    [InlineData("float", "<= double tenth", false, "Tenth", null)]
    [InlineData("decimal", "between", false, "DecimalUnit", null)]
    [InlineData("decimal", "between reversed", false, "DecimalUnit", null)]
    [InlineData("decimal", "not positive", false, "DecimalNonPositive", null)]
    [InlineData("decimal", "> 0", true, "DecimalNonPositive", null)]
    [InlineData("decimal", "<= 0", true, "DecimalPositive", null)]
    [InlineData("double", "not positive", false, "NonPositive", "MIST0272")]
    [InlineData("double", "not nonpositive", false, "Positive", "MIST0272")]
    [InlineData("double", "> 0", true, "NonPositive", "MIST0272")]
    [InlineData("double", "neither outside comparison", false, "Unit", "MIST0272")]
    [InlineData("double", ">= 0", false, "Unit", "MIST0272")]
    [InlineData("double", "between", false, "StrictUnit", "MIST0272")]
    [InlineData("float", "<= float tenth", false, "Tenth", "MIST0272")]
    [InlineData("double", "above another parameter", false, "Positive", "MIST0272")]
    public void TheTakenRealGuardDeterminesWhichRefinementsHold(string domain, string guard, bool earlyReturn, string target, string? expected)
    {
        var r = new RealFlowBody();
        var input = Local(r.Type(domain), "input");
        r.Check(expected, Guarded(r.Guard(domain, input, guard), Need(input, r.Type(target)), earlyReturn));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void AnExplicitRefinementCastAcceptsALiteralOrAGuardedInput(bool guarded)
    {
        var r = new RealFlowBody();
        var input = Local(r.Values.Double, "input");
        var positive = r.Type("Positive");
        var assignment = Need(Cast(guarded ? input : r.Values.Literal(2.5), positive), positive);
        r.Check(null, guarded ? If(r.Positive(input), assignment) : assignment);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void ARealPatternProvesTheTestedValueOrItsBinding(bool bindsName)
    {
        var r = new RealFlowBody();
        var h = r.Values;
        var input = Local(h.Double, "input");
        var unit = Assert.IsType<RefinedType>(r.Type("Unit"));
        var narrowed = Local(unit, "narrowed");
        var pattern = bindsName ? new TypeTestPattern(Node(), unit, narrowed.Local, refinement: unit.Predicate) : unit.Predicate!;
        r.Check(null, If(new IsPatternExpression(Node(), input, pattern, h.Bool),
            bindsName ? Need(narrowed, r.Type("NonNegative")) : Need(input, unit)));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("double", "join", false, null)]
    [InlineData("double", "join", true, null)]
    [InlineData("double", "if and else", false, null)]
    [InlineData("float", "join", true, null)]
    [InlineData("decimal", "join", false, null)]
    [InlineData("double", "loop", false, null)]
    [InlineData("double", "join", false, "MIST0272")]
    public void AJoinOrLoopMustEstablishTheBoundOnEveryPath(string domain, string shape, bool copyToDouble, string? expected)
    {
        var r = new RealFlowBody();
        var number = Local(r.Type(domain), "number");
        var flag = Local(r.Values.Bool, "flag");
        var low = r.Number(domain, 0.25);
        var high = r.Number(domain, expected is null ? 0.75 : 1.5);
        var target = r.Type(domain == "decimal" ? "DecimalUnit" : "Unit");
        var copy = Local(r.Values.Double, "copy");
        Statement use = copyToDouble ? Block(Give(copy, number), Need(copy, target)) : Need(number, target);
        var assign = Set(number, high);
        switch(shape)
        {
            case "join":
                r.Check(expected, Give(number, low), If(flag, assign), use);
                break;
            case "if and else":
                r.Check(expected, new IfStatement(flag, Block(Set(number, low)), Block(assign)), use);
                break;
            case "loop":
                r.Check(expected, Give(number, low), new WhileStatement(flag, Block(assign, Set(flag, r.Values.Literal(false)))), use);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(shape));
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("overwrite source", null)]
    [InlineData("copy before guard", null)]
    [InlineData("overwrite input", "MIST0272")]
    [InlineData("overwrite copy", "MIST0272")]
    [InlineData("add one", "MIST0272")]
    [InlineData("overwrite with negative one", "MIST0271")]
    public void AGuardProtectsOnlyTheValueThatWasTested(string change, string? expected)
    {
        var r = new RealFlowBody();
        var h = r.Values;
        var input = Local(h.Double, "input");
        var other = Local(h.Double, "other");
        var copy = Local(h.Double, "copy");
        var positive = r.Type("Positive");
        var guard = r.Positive(input);
        Statement body = change switch
        {
            "overwrite source" => If(guard, Give(copy, input), Set(input, other), Need(copy, positive)),
            "copy before guard" => Block(Give(copy, input), If(guard, Need(copy, positive))),
            "overwrite input" => If(guard, Set(input, other), Need(input, positive)),
            "overwrite copy" => If(guard, Give(copy, input), Set(copy, other), Need(copy, positive)),
            "add one" => If(guard, Give(copy, h.Binary(input, BinaryOperatorKind.Add, h.Literal(1.0), h.Double)), Need(copy, positive)),
            "overwrite with negative one" => If(guard, Set(input, new UnaryExpression(Node(), UnaryOperatorKind.Negate, h.Literal(1.0), h.Double)), Need(input, positive)),
            _ => throw new ArgumentOutOfRangeException(nameof(change)),
        };
        r.Check(expected, body);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("lambda reads", null)]
    [InlineData("lambda writes unrelated local", null)]
    [InlineData("lambda writes input", "MIST0272")]
    [InlineData("local function writes input", "MIST0272")]
    public void AClosureInvalidatesOnlyTheRealLocalItCanWrite(string capture, string? expected)
    {
        var r = new RealFlowBody();
        var h = r.Values;
        var input = Local(h.Double, "input");
        var other = Local(h.Double, "other");
        var unrelated = Local(h.Double, "unrelated");
        var resultType = capture == "lambda reads" ? h.Double : h.Built.Type("Void");
        var functionType = new FunctionType([], resultType);
        var function = Local(functionType, "action");
        var statements = new List<Statement>();
        if(capture == "lambda reads")
        {
            statements.Add(Give(function, new LambdaExpression(Node(), [], input, null, false, functionType)));
            statements.Add(Give(Local(resultType, "ignored"), new InvocationExpression(Node(), function, [], resultType, null, [])));
        }
        else
        {
            var target = input;
            if(capture == "lambda writes unrelated local")
            {
                statements.Add(Give(unrelated, other));
                target = unrelated;
            }

            var write = Block(Set(target, other));
            if(capture == "local function writes input")
            {
                var method = new Method("Write", resultType, [], new MemberModifiers()) { Body = write };
                statements.Add(new LocalFunctionStatement(method, []));
                statements.Add(Evaluate(Invoke(method)));
            }
            else
            {
                statements.Add(Give(function, new LambdaExpression(Node(), [], null, write, false, functionType)));
                statements.Add(Evaluate(new InvocationExpression(Node(), function, [], resultType, null, [])));
            }
        }

        statements.Add(Need(input, r.Type("Positive")));
        r.Check(expected, If(r.Positive(input), [.. statements]));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("argument")]
    [InlineData("where")]
    [InlineData("return")]
    [InlineData("unproven where")]
    public void ARealGuardReachesArgumentsConstraintsAndReturns(string site)
    {
        var r = new RealFlowBody();
        var h = r.Values;
        var input = Local(h.Double, "input");
        var target = site switch
        {
            "where" => h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0, null),
            "unproven where" => h.Refine(h.Double, ComparisonKind.LessThan, 0.0, null),
            _ => r.Type("Positive"),
        };
        var parameter = new Parameter("p", site == "return" ? h.Double : target);
        var local = new LocalVariable("p", parameter.Type) { Parameter = parameter };
        var p = Read(local);
        if(site == "return")
        {
            var method = new Method("Read", target, [parameter], new MemberModifiers())
            { Body = Block(If(r.Positive(p), new ReturnStatement(p, target)), new ReturnStatement(h.Literal(1.0), target)) };
            r.Check(null, new LocalFunctionStatement(method, [local]));
            return;
        }

        var refused = site == "unproven where";
        var result = refused ? h.Built.Type("Void") : h.Double;
        var take = new Method("Take", result, [parameter], new MemberModifiers())
        { Body = refused ? Block() : Block(new ReturnStatement(p, result)) };
        var declare = new LocalFunctionStatement(take, [local]);
        var call = Invoke(take, input);
        if(refused)
        {
            r.Check("MIST0277", If(h.Binary(input, BinaryOperatorKind.GreaterThanOrEqual, h.Literal(0.0)), new ReturnStatement(null, null)),
                declare, Evaluate(call));
        }
        else
        {
            r.Check(null, declare, If(r.Positive(input), Give(Local(result), call)));
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnguardedValueMustMeetTheWholeTargetRefinement(bool widened)
    {
        var r = new RealFlowBody();
        var unit = r.Type("Unit");
        var value = Local(unit, "value");
        if(widened)
        {
            r.Check("MIST0272", Give(value, Local(r.Type("PositiveUnit"))), Need(value, r.Type("StrictUnit")));
        }
        else
        {
            r.Check("MIST0272", Give(value, Local(r.Values.Double)));
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void AnArithmeticResultCannotReuseItsOperandsRefinement(bool cast)
    {
        var r = new RealFlowBody();
        var positive = r.Type("Positive");
        var parameter = new Parameter("first", positive);
        var local = new LocalVariable(parameter.Name, positive) { Parameter = parameter };
        var first = Read(local);
        var difference = r.Values.Binary(first, BinaryOperatorKind.Subtract, first, r.Values.Double);
        var use = cast ? Give(Local(positive, "value"), Cast(difference, positive)) : Need(difference, positive);
        var function = new Method("Forward", r.Values.Built.Type("Void"), [parameter], new MemberModifiers()) { Body = Block(use) };
        r.Check("MIST0272", new LocalFunctionStatement(function, [local]));
    }

    [Theory]
    [Trait("Tier", "4")]
    [InlineData("if(single <= 0.1) { FloatTenth value = single; }", null)]
    [InlineData("if(input >= 0.0 && input <= 1.0) { float narrowed = (float)input; FloatUnit value = narrowed; }", "MIST0149")]
    [InlineData("if(amount >= 0.0m && amount <= 1.0m) { double converted = (double)amount; Unit value = converted; }", "MIST0149")]
    public void BoundNumericConversionsPreserveOnlySupportedRealFacts(string body, string? castDiagnostic)
    {
        var source = $$"""
            type Positive = double > 0.0;
            type NonNegative = double >= 0.0;
            type NonPositive = double <= 0.0;
            type Unit = double >= 0.0 and <= 1.0;
            type PositiveUnit = Unit and > 0.0;
            type StrictUnit = double > 0.0 and < 1.0;
            type Outside = double (< 0.0 or > 1.0);
            type NotZero = double not 0.0;
            type NotPositive = double not > 0.0;
            type FloatUnit = float >= 0.0f and <= 1.0f;
            type FloatTenth = float <= 0.1;
            type Tenth = double <= 0.1;
            type DecimalUnit = decimal >= 0.0m and <= 1.0m;
            type DecimalNonNegative = decimal >= 0.0m;
            type DecimalNonPositive = decimal <= 0.0m;
            type DecimalPositive = decimal > 0.0m;
            public class RealCases
            {
                public static void Check(double input, double other, double limit, float single, decimal amount,
                    bool flag, Unit unit, PositiveUnit positiveUnit, FloatUnit floatUnit, DecimalUnit decimalUnit)
                {
                    {{body}}
                }
            }
            """;
        var model = StandardModelProbe.Build("", out var buildDiagnostics, source);
        Assert.Empty(buildDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var declaring = Assert.IsType<ClassType>(model.TypesByFQN["Probe.RealCases"]);
        var nodes = BodyWalker.DescendantsAndSelf(Assert.IsType<Block>(declaring.MethodsList.Single(method => method.Name == "Check").Body));
        Assert.DoesNotContain(nodes.OfType<Expression>(), expression => expression.Type is ErrorType);
        var findings = new DiagnosticBag();
        ProgramAnalysisRunner.Run(model, findings);

        if(castDiagnostic is null)
        {
            Assert.Empty(findings.ToImmutableArray());
        }
        else
        {
            Assert.Equal([castDiagnostic, "MIST0272"], findings.ToImmutableArray().Select(diagnostic => diagnostic.Number.Value).Order());
        }
    }
}