using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class PatternAssignmentFixture
{
    private readonly HandBuiltModel model = new();

    public IMistType Integer { get; } = HandBuiltModel.Struct("Int32");

    public IMistType Boolean { get; } = HandBuiltModel.Struct("Boolean");

    public LocalVariable Variable(string name = "matched", IMistType? type = null)
    {
        return new LocalVariable(name, type ?? Integer);
    }

    public Expression Flag()
    {
        return Read(Variable("flag", Boolean), offset: 0);
    }

    public static LocalReferenceExpression Read(LocalVariable variable, int offset = 37, IMistType? narrowed = null)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), variable, narrowed);
    }

    public IsPatternExpression Match(LocalVariable variable)
    {
        return Test(new TypeTestPattern(HandBuiltModel.Node(), variable.Type, variable));
    }

    public IsPatternExpression Test(Pattern pattern)
    {
        return new IsPatternExpression(HandBuiltModel.Node(), Read(Variable("input", pattern.NarrowedType), offset: 0), pattern, Boolean);
    }

    public IMistType ArrayOf(IMistType element)
    {
        return model.Interner.GetOrCreateConstructedType(model.Type("Array<,>"), [element, model.Interner.GetOrCreateUnknownValue(Integer)]);
    }

    public TupleLiteralExpression Pair()
    {
        return new TupleLiteralExpression(HandBuiltModel.Node(), [HandBuiltModel.Literal(Integer, 1L), HandBuiltModel.Literal(Integer, 2L)], [null, null],
            model.Interner.GetOrCreateTupleType([Integer, Integer]));
    }

    public BinaryExpression And(Expression left, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.LogicalAnd, right, Boolean);
    }

    public BinaryExpression Or(Expression left, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.LogicalOr, right, Boolean);
    }

    public UnaryExpression Not(Expression operand)
    {
        return new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.LogicalNot, operand, Boolean);
    }

    public BinaryExpression Positive(LocalVariable variable)
    {
        return new BinaryExpression(HandBuiltModel.Node(), Read(variable), BinaryOperatorKind.GreaterThan,
            HandBuiltModel.Literal(Integer, 0L), Boolean);
    }

    public static ExpressionStatement Use(Expression expression)
    {
        return new ExpressionStatement(expression);
    }

    public static IfStatement If(Expression condition, params Statement[] statements)
    {
        return new IfStatement(condition, HandBuiltModel.Block(statements), null, conditionBinds: true);
    }

    public static ExpressionStatement Write(LocalVariable variable, Expression? value = null)
    {
        return Use(new AssignmentExpression(HandBuiltModel.Node(), Read(variable, offset: 0),
            value ?? HandBuiltModel.Literal(variable.Type, 1L), variable.Type));
    }

    public IReadOnlyList<Diagnostic> Analyze(params Statement[] statements)
    {
        return model.Analyze(new DefiniteAssignmentAnalyzer(), HandBuiltModel.Block(statements));
    }

    public LocalFunctionStatement Function(Block body, string name = "Read", IReadOnlyList<LocalVariable>? parameters = null)
    {
        var arguments = parameters ?? [];
        var method = new Method(name, Integer, [.. arguments.Select(argument => new Parameter(argument.Name, argument.Type))], default)
        {
            Body = body,
        };
        return new LocalFunctionStatement(method, arguments);
    }

    public InvocationExpression Call(LocalFunctionStatement function, int offset = 37)
    {
        return new InvocationExpression(HandBuiltModel.Node(offset),
            new MethodReferenceExpression(HandBuiltModel.Node(), function.Function), [], Integer, function.Function, []);
    }

    public FunctionReferenceExpression Reference(LocalFunctionStatement function)
    {
        return new FunctionReferenceExpression(HandBuiltModel.Node(37), null, function.Function,
            model.Interner.GetOrCreateFunctionType([], Integer));
    }

    public Block Wrap(string kind, Block body)
    {
        if(kind == "function")
        {
            var function = Function(body);
            return HandBuiltModel.Block(function, Use(Call(function)));
        }

        Expression expression = kind switch
        {
            "lambda" or "async-lambda" => new LambdaExpression(HandBuiltModel.Node(), [], null, body, false,
                model.Interner.GetOrCreateFunctionType([], Integer))
            { IsAsync = kind == "async-lambda" },
            "async" => new AsyncBlockExpression(HandBuiltModel.Node(), body, model.Type("Task"), false),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return HandBuiltModel.Block(Use(expression));
    }

    public (Pattern Pattern, IReadOnlyList<LocalVariable> Variables) NestedPattern(string kind)
    {
        var first = Variable("first");
        var second = Variable("second");
        var firstPattern = new TypeTestPattern(HandBuiltModel.Node(), Integer, first);
        var secondPattern = new CatchAllPattern(HandBuiltModel.Node(), Integer, second);
        var pair = model.Interner.GetOrCreateTupleType([Integer, Integer]);
        var whole = Variable("whole", pair);
        var tuple = new TuplePattern(HandBuiltModel.Node(), [firstPattern, secondPattern], pair, null, whole);

        switch(kind)
        {
            case "var":
                return (new CatchAllPattern(HandBuiltModel.Node(), Integer, first), [first]);
            case "tuple":
                return (tuple, [first, second, whole]);
            case "conjunction":
                return (new ConjunctionPattern(HandBuiltModel.Node(), Integer, [firstPattern, secondPattern]), [first, second]);
            case "property":
            case "list-property-tuple":
                var holder = HandBuiltModel.Class("Holder");
                var value = new Field("Value", pair, default, default) { DeclaringType = holder };
                holder.FieldsList.Add(value);
                var holderBinding = Variable("holder", holder);
                var property = new PropertyPattern(HandBuiltModel.Node(), holder,
                    [new PropertySubpattern(HandBuiltModel.Node(), value.Name, tuple, pair, new FieldResult(value, holder, []))], holderBinding);
                return kind == "property"
                    ? (property, [first, second, whole, holderBinding])
                    : (new ListPattern(HandBuiltModel.Node(), [new ListPatternElement(property, false)], ArrayOf(holder)),
                        [first, second, whole, holderBinding]);
            case "list-slice":
                var slice = Variable("slice", ArrayOf(Integer));
                return (new ListPattern(HandBuiltModel.Node(),
                    [new ListPatternElement(firstPattern, false),
                     new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), slice.Type, slice), true)], ArrayOf(Integer)),
                    [first, slice]);
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    public (LocalVariable Variable, Field First, Field Second) StructVariable()
    {
        var point = new StructType("Point", "Probe", new Scope(null), null, new BodyTypeTypeDeclarationNode());
        var first = new Field("first", Integer, default, default) { DeclaringType = point };
        var second = new Field("second", Integer, default, default) { DeclaringType = point };
        point.FieldsList.Add(first);
        point.FieldsList.Add(second);
        return (Variable("point", point), first, second);
    }

    public static MemberAccessExpression FieldRead(LocalVariable variable, Field field)
    {
        return new MemberAccessExpression(HandBuiltModel.Node(37), Read(variable), field.Name, field.Type,
            member: new FieldResult(field, variable.Type, []));
    }
}