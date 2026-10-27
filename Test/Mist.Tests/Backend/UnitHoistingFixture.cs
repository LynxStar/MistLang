using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Backend;

public enum UnitHoistingScenario
{
    CallArgument,
    InterpolatedHole,
    AssignedUnit,
    LocalRead,
    EarlierCall,
    EarlierField,
    FieldReceiver,
    ReferenceArgument,
    ValueReceiver,
    WhileCondition,
    ElseIfCondition,
    GenericUnitResult,
    LogicalAnd,
    LogicalOr,
    CoalescedValue,
    CoalescingAssignment,
    ConditionalReceiver,
    SwitchArm,
    ConditionalFactory,
    CoalescingReceiver,
    PatternIf,
    PatternWhile,
    ForCondition,
    ForIterator,
    ExpressionLambda,
    ConditionalLambda,
    SwitchGuard,
    NegatedPattern,
}

internal sealed class UnitHoistingFixture
{
    private readonly HandBuiltModel built = new();
    private readonly StructType number = new("Int32", "Standard", null, null, null);
    private readonly StructType boolean = new("Boolean", "Standard", null, null, null);
    private readonly StructType unit = new("Void", "Standard", null, null, null);
    private readonly ClassType log = Class("Log");
    private readonly StructType spot = Struct("Spot");
    private readonly Dictionary<string, Method> methods = [];
    private readonly Dictionary<string, LocalVariable> locals = [];
    private readonly Field trail;
    private readonly Field point;
    private readonly Field name;
    private readonly Field optional;

    public UnitHoistingFixture()
    {
        var self = built.Model.AddSelfAssembly("Probe", isLibrary: true);
        log.Assembly = self;
        spot.Assembly = self;
        unit.Assembly = built.Model.AddStandardAssembly();
        trail = Field(log, "Trail", Text, isStatic: true);
        point = Field(log, "Pt", spot);
        name = Field(spot, "Name", Text);
        optional = Field(log, "Opt", Optional(Text));
        Add("F", Void);
        Add("H", Number);
        Add("K", Number);
        Add("G", Text, Void, Number);
        Add("G3", Text, Number, Void, Number);
        Add("Read", Text, Text, Void);
        Add("Check", Boolean, Void);
        Add("Below", Boolean, Void, Number);
        Add("Next", Number, Void, Number);
        Add("Show", Text, Void);
        Add("Bump", Void, Number, Void);
        methods["Bump"].ParametersList[0] = new Parameter("slot", Number, new ParameterModifier(false, true, default, ByRefDirection.Ref));
        Add("Make", log);
        Add("MakeSpot", spot);
        Add("Maybe", Optional(log));
        var echoed = new TypeParameter("T", 0, new Scope(null), log);
        var echo = new Method("Echo", echoed, [new Parameter("value", echoed)], new MemberModifiers { IsStatic = true }) { DeclaringType = log };
        echoed.DeclaringMethod = echo;
        echo.TypeParametersList.Add(echoed);
        methods["Echo"] = echo;
        Add("Take", Text, Void);
        Add("Has", Boolean, Void);
        Add("Poke", Void, Void);
        methods["Describe"] = new Method("Describe", Text, [new Parameter("unit", Void)], default) { DeclaringType = spot };
    }

    public ProgramModel.Model.ProgramModel Model => built.Model;

    private IMistType Number => number;

    private IMistType Text => built.Type("String");

    private IMistType Boolean => boolean;

    private IMistType Void => unit;

    public Block Block(UnitHoistingScenario scenario)
    {
        switch(scenario)
        {
            case UnitHoistingScenario.CallArgument:
                return HandBuiltModel.Block(Declare("called", Call("G", F(), Call("H"))));
            case UnitHoistingScenario.InterpolatedHole:
                return HandBuiltModel.Block(Declare("hole", new InterpolatedStringExpression(Node(),
                    [new InterpolatedStringText("hole="), new InterpolatedStringHole(F()), new InterpolatedStringText(" "), new InterpolatedStringHole(Call("H"))], Text)));
            case UnitHoistingScenario.AssignedUnit:
                return HandBuiltModel.Block(Declare("unit", Unit()), Run(Assign(Ref("unit"), F())));
            case UnitHoistingScenario.LocalRead:
                return HandBuiltModel.Block(Declare("seen", Literal(Text, "x")), Declare("read", Call("Read", Ref("seen"), F())));
            case UnitHoistingScenario.EarlierCall:
                return HandBuiltModel.Block(Declare("ordered", Call("G3", Call("K"), F(), Call("H"))));
            case UnitHoistingScenario.EarlierField:
                return HandBuiltModel.Block(Declare("read", Call("Read", new FieldReferenceExpression(Node(), trail), F())));
            case UnitHoistingScenario.FieldReceiver:
                return HandBuiltModel.Block(Run(Assign(Member(Member(Call("Make"), point), name), Show())));
            case UnitHoistingScenario.ReferenceArgument:
                var array = SizedSequenceTypes.ArrayOf(Number, null);
                return HandBuiltModel.Block(Declare("slots", new ArrayLiteralExpression(Node(), Number, [Literal(Number, 5L)], array)),
                    Run(Call("Bump", new IndexExpression(Node(), Ref("slots"), Call("K"), Number), F())));
            case UnitHoistingScenario.ValueReceiver:
                return HandBuiltModel.Block(Declare("text", On(Call("MakeSpot"), "Describe", F())));
            case UnitHoistingScenario.WhileCondition:
                return HandBuiltModel.Block(new WhileStatement(Call("Below", F(), Literal(Number, 2L)), HandBuiltModel.Block()));
            case UnitHoistingScenario.ElseIfCondition:
                var value = Declare("value", Literal(Number, 3L));
                return HandBuiltModel.Block(value, new IfStatement(Binary(Ref("value"), BinaryOperatorKind.Equal, Literal(Number, 1L)),
                    HandBuiltModel.Block(), new IfStatement(Call("Check", F()), HandBuiltModel.Block(), null)));
            case UnitHoistingScenario.GenericUnitResult:
                var flag = Declare("flag", Literal(Boolean, true));
                var echo = new InvocationExpression(Node(), new MemberAccessExpression(Node(), new TypeReferenceExpression(Node(), log), "Echo", Void, [Void]),
                    [Unit()], Void, methods["Echo"], [Void]);
                return HandBuiltModel.Block(flag, Declare("checkedIt", Binary(Ref("flag"), BinaryOperatorKind.LogicalAnd, Call("Check", echo))));
            case UnitHoistingScenario.LogicalAnd:
            case UnitHoistingScenario.LogicalOr:
                var declared = Declare("flag", Literal(Boolean, true));
                return HandBuiltModel.Block(declared, Declare("held", Binary(Ref("flag"),
                    scenario == UnitHoistingScenario.LogicalAnd ? BinaryOperatorKind.LogicalAnd : BinaryOperatorKind.LogicalOr, Call("Check", F()))));
            case UnitHoistingScenario.CoalescedValue:
            case UnitHoistingScenario.CoalescingAssignment:
                var none = Declare("none", Absent(Text));
                var coalesced = scenario == UnitHoistingScenario.CoalescedValue
                    ? Declare("text", new CoalesceExpression(Node(), Ref("none"), Show(), Text))
                    : (Statement)Run(new AssignmentExpression(Node(), Ref("none"), Show(), Text, isCoalescing: true));
                return HandBuiltModel.Block(none, coalesced);
            case UnitHoistingScenario.ConditionalReceiver:
                var maybe = Declare("maybe", Absent(log));
                return HandBuiltModel.Block(maybe, Declare("taken", On(Ref("maybe"), "Take", F(), conditional: true)));
            case UnitHoistingScenario.SwitchArm:
            case UnitHoistingScenario.SwitchGuard:
                var one = Declare("one", Literal(Number, 1L));
                var arms = new SwitchArm[]
                {
                    new(Node(), new ComparisonPattern(Node(), Number, ComparisonKind.Equal, Literal(Number, 1L)),
                        scenario == UnitHoistingScenario.SwitchGuard ? Call("Check", F()) : null, scenario == UnitHoistingScenario.SwitchGuard ? Literal(Text, "a") : Show()),
                    new(Node(), new CatchAllPattern(Node(), Number, null), null, Literal(Text, scenario == UnitHoistingScenario.SwitchGuard ? "b" : "none")),
                };
                return HandBuiltModel.Block(one, Declare(scenario == UnitHoistingScenario.SwitchGuard ? "armed" : "text", new SwitchExpression(Node(), Ref("one"), arms, Text)));
            case UnitHoistingScenario.ConditionalFactory:
                return HandBuiltModel.Block(Declare("taken", On(Call("Maybe"), "Take", F(), conditional: true)));
            case UnitHoistingScenario.CoalescingReceiver:
                return HandBuiltModel.Block(Run(new AssignmentExpression(Node(), Member(Call("Make"), optional), Show(), Text, isCoalescing: true)));
            case UnitHoistingScenario.PatternIf:
            case UnitHoistingScenario.NegatedPattern:
                var o = Declare("o", Literal(UnknownType.Instance, "x"));
                var binding = Local("l", log);
                Expression condition = Binary(new IsPatternExpression(Node(), Ref("o"), new TypeTestPattern(Node(), log, binding), Boolean),
                    BinaryOperatorKind.LogicalAnd, On(Ref("l"), "Has", F()));
                if(scenario == UnitHoistingScenario.NegatedPattern)
                {
                    condition = new UnaryExpression(Node(), UnaryOperatorKind.LogicalNot,
                        new UnaryExpression(Node(), UnaryOperatorKind.LogicalNot, condition, Boolean), Boolean);
                }

                return HandBuiltModel.Block(o, new IfStatement(condition, HandBuiltModel.Block(Run(On(Ref("l"), "Poke", Unit()))),
                    scenario == UnitHoistingScenario.PatternIf ? HandBuiltModel.Block(Run(F())) : null, conditionBinds: true));
            case UnitHoistingScenario.PatternWhile:
                var l = Local("l", log);
                return HandBuiltModel.Block(new WhileStatement(Binary(new IsPatternExpression(Node(), Call("Maybe"),
                    new TypeTestPattern(Node(), log, l), Boolean), BinaryOperatorKind.LogicalAnd, On(Ref("l"), "Has", F())),
                    HandBuiltModel.Block(Run(On(Ref("l"), "Poke", Unit()))), conditionBinds: true));
            case UnitHoistingScenario.ForCondition:
            case UnitHoistingScenario.ForIterator:
                var i = Declare("i", Literal(Number, 0L));
                var test = scenario == UnitHoistingScenario.ForCondition ? (Expression)Call("Below", F(), Ref("i")) : Binary(Ref("i"), BinaryOperatorKind.LessThan, Literal(Number, 2L));
                var next = scenario == UnitHoistingScenario.ForCondition ? new BinaryExpression(Node(), Ref("i"), BinaryOperatorKind.Add, Literal(Number, 1L), Number)
                    : (Expression)Call("Next", F(), Ref("i"));
                return HandBuiltModel.Block(new ForStatement(i, test, Assign(Ref("i"), next), HandBuiltModel.Block()));
            case UnitHoistingScenario.ExpressionLambda:
                return HandBuiltModel.Block(Declare("fn", new LambdaExpression(Node(), [], Show(), null, false, new FunctionType([], Text))));
            case UnitHoistingScenario.ConditionalLambda:
                var maybeLocal = Declare("maybe", Absent(log));
                return HandBuiltModel.Block(maybeLocal, Declare("act", new LambdaExpression(Node(), [],
                    On(Ref("maybe"), "Poke", F(), conditional: true), null, false, new FunctionType([], Void))));
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    public MemberType Hosts()
    {
        var parent = Class("Base");
        parent.Assembly = built.Model.SelfAssembly!;
        var host = Class("Hosts", parent);
        host.Assembly = built.Model.SelfAssembly!;
        host.Modifiers = new MemberModifiers { Accessibility = Accessibility.Public };
        var visibility = new MemberModifiers { Accessibility = Accessibility.Public };
        host.FieldsList.Add(new Field("Seeded", Text, default, visibility with { IsStatic = true }) { DeclaringType = host, Initializer = Show() });
        host.PropertiesList.Add(new Property("Held", Text, visibility) { DeclaringType = host, Getter = new PropertyAccessor { IsAuto = true }, Initializer = Show() });
        host.PropertiesList.Add(new Property("Shown", Text, visibility) { DeclaringType = host, Getter = new PropertyAccessor { ExpressionBody = Show() } });
        var parameter = new Parameter("n", Number);
        var baseConstructor = new Constructor("Base", parent, [new Parameter("seen", Text)], visibility) { DeclaringType = parent, Body = HandBuiltModel.Block() };
        var constructor = new Constructor("Hosts", host, [parameter], visibility)
        {
            DeclaringType = host,
            Body = HandBuiltModel.Block(),
            ChainKind = ConstructorChainKind.Base,
            ChainedConstructor = baseConstructor,
            ChainParameterTypes = [Text],
        };
        var local = new LocalVariable("n", Number) { Parameter = parameter };
        constructor.ChainArgumentsList.Add(Call("G", F(), new LocalReferenceExpression(Node(), local)));
        host.ConstructorsList.Add(constructor);
        return host;
    }

    private void Add(string name, IMistType result, params IMistType[] parameters)
    {
        methods[name] = new Method(name, result, [.. parameters.Select((type, i) => new Parameter("p" + i, type))],
            new MemberModifiers { IsStatic = name is not ("Take" or "Has" or "Poke") })
        { DeclaringType = log };
    }

    private InvocationExpression F()
    {
        return Call("F");
    }

    private InvocationExpression Show()
    {
        return Call("Show", F());
    }

    private InvocationExpression Call(string method, params Expression[] arguments)
    {
        return Invoke(new TypeReferenceExpression(Node(), log), methods[method], arguments);
    }

    private InvocationExpression On(Expression receiver, string method, Expression argument, bool conditional = false)
    {
        return Invoke(receiver, methods[method], [argument], conditional);
    }

    private InvocationExpression Invoke(Expression receiver, Method method, Expression[] arguments, bool conditional = false)
    {
        var result = conditional && method.ReturnType.Name != "Void" ? Optional(method.ReturnType) : method.ReturnType;
        return new InvocationExpression(Node(), new MemberAccessExpression(Node(), receiver, method.Name, result, isConditional: conditional),
            arguments, result, method, [.. method.ParametersList.Select(parameter => parameter.Type)]);
    }

    private LocalVariable Local(string name, IMistType type)
    {
        var local = new LocalVariable(name, type);
        locals.Add(name, local);
        return local;
    }

    private LocalDeclarationStatement Declare(string name, Expression initializer)
    {
        return new LocalDeclarationStatement(Local(name, initializer.Type), initializer);
    }

    private LocalReferenceExpression Ref(string name)
    {
        return new LocalReferenceExpression(Node(), locals[name]);
    }

    private Expression Unit()
    {
        return new VoidLiteralExpression(Node(), VoidReading.Singleton, Void);
    }

    private Expression Absent(IMistType type)
    {
        return new VoidLiteralExpression(Node(), VoidReading.Absence, Optional(type));
    }

    private IMistType Optional(IMistType type)
    {
        return built.Interner.GetOrCreateUnionType([type, Void]);
    }

    private BinaryExpression Binary(Expression left, BinaryOperatorKind kind, Expression right)
    {
        return new BinaryExpression(Node(), left, kind, right, Boolean);
    }

    private static ExpressionStatement Run(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    private static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new AssignmentExpression(Node(), target, value, target.Type);
    }

    private static Field Field(MemberType owner, string name, IMistType type, bool isStatic = false)
    {
        return new Field(name, type, default, new MemberModifiers { IsStatic = isStatic }) { DeclaringType = owner };
    }

    private static MemberAccessExpression Member(Expression receiver, Field field)
    {
        return new MemberAccessExpression(Node(), receiver, field.Name, field.Type,
            member: new FieldResult(field, field.DeclaringType!, []));
    }
}