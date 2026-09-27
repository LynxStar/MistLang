using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class HandBuiltRefinements
{
    public HandBuiltModel Built { get; } = new();
    public IMistType Int => Built.Type("Int32");
    public IMistType Bool => Built.Type("Boolean");
    public IMistType Double => Built.Type("Double");
    public IMistType String => Built.Type("String");
    public IMistType Index => Built.Type("Index");
    public IMistType Range => Built.Type("Range");

    public LiteralExpression Literal(object value, IMistType? type = null, int offset = 0)
    {
        type ??= value switch
        {
            float => Built.Type("Single"),
            double => Double,
            decimal => Built.Type("Decimal"),
            bool => Bool,
            char => Built.Type("Char"),
            string => String,
            _ => Int,
        };
        return HandBuiltModel.Literal(type, value, offset);
    }

    public ComparisonPattern Compare(IMistType type, ComparisonKind kind, object bound)
    {
        return new ComparisonPattern(HandBuiltModel.Node(), type, kind, bound is Expression expression ? expression : Literal(bound));
    }

    public static RefinedType RefinePattern(IMistType type, Pattern predicate, string? name = "Refined")
    {
        return new RefinedType(type, HandBuiltModel.Node(), name) { Predicate = predicate };
    }

    public RefinedType Refine(IMistType type, ComparisonKind kind, object bound, string? name = "Refined")
    {
        return RefinePattern(type, Compare(type, kind, bound), name);
    }

    public RefinedType Between(IMistType type, object low, object high, string? name = "Between")
    {
        return RefinePattern(type, new ConjunctionPattern(HandBuiltModel.Node(), type,
            [Compare(type, ComparisonKind.GreaterThanOrEqual, low), Compare(type, ComparisonKind.LessThanOrEqual, high)]), name);
    }

    public RefinedType Regex(string regex, string? name = "Regex")
    {
        return RefinePattern(String, new RegexPattern(HandBuiltModel.Node(), String, regex), name);
    }

    public IMistType Sequence(string name = "Array", long? length = null)
    {
        var definition = Built.Type(name + "<,>");
        if(definition.TypeParametersList.Count == 0)
        {
            definition.TypeParametersList.Add(new TypeParameter("Element", 0, definition.Scope, definition));
            definition.TypeParametersList.Add(new TypeParameter("Length", 1, definition.Scope, definition) { IsDependent = true, Domain = Built.Type("Int32") });
        }

        return Built.Interner.GetOrCreateConstructedType(definition,
            [Int, length is { } known ? new LiftedType(Int, known) : Built.Type("Unknown")]);
    }

    public Expression Array(long size, string name = "Array", bool erased = false)
    {
        var type = Sequence(name, erased ? null : size);
        return name == "Array" ? new ArrayCreationExpression(HandBuiltModel.Node(), Int, Literal(size), type)
            : new StackallocExpression(HandBuiltModel.Node(), Int, Literal(size), type);
    }

    public static LocalReferenceExpression Local(IMistType type, string name = "value", int offset = 0)
    {
        return Read(new LocalVariable(name, type), offset);
    }

    public static LocalReferenceExpression Read(LocalVariable local, int offset = 0)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(offset), local);
    }

    public static ExpressionStatement Evaluate(Expression value)
    {
        return new(value);
    }

    public static LocalDeclarationStatement Give(LocalReferenceExpression local, Expression value)
    {
        return new(local.Local, value);
    }

    public static LocalDeclarationStatement Need(Expression value, IMistType target)
    {
        return new(new LocalVariable("required", target), value);
    }

    public static AssignmentExpression Assign(Expression target, Expression value)
    {
        return new(HandBuiltModel.Node(), target, value, target.Type);
    }

    public UnaryExpression Back(Expression offset)
    {
        return new(HandBuiltModel.Node(), UnaryOperatorKind.FromEnd, offset, Index);
    }

    public UnaryExpression Not(Expression operand)
    {
        return new(HandBuiltModel.Node(), UnaryOperatorKind.LogicalNot, operand, Bool);
    }

    public Expression Binary(Expression left, BinaryOperatorKind kind, Expression right, IMistType? type = null)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, kind, right, type ?? Bool);
    }

    public Expression And(Expression left, Expression right)
    {
        return Binary(left, BinaryOperatorKind.LogicalAnd, right);
    }

    public MemberAccessExpression Length(Expression receiver)
    {
        var member = receiver.Type is ConstructedType constructed
            ? (LookupResult)new TypeResult(((ClassType)constructed.Definition).TypeParametersList[1])
            : new PropertyResult(new Property("Length", Int, new MemberModifiers { IsIntrinsic = true }) { DeclaringType = Built.Type("String") }, Built.Type("String"), []);
        return new MemberAccessExpression(HandBuiltModel.Node(), receiver, "Length", Int, member: member);
    }
    public IndexExpression At(Expression receiver, Expression index, int offset = 0)
    {
        return new(HandBuiltModel.Node(offset), receiver, index, Int) { ParameterType = Int };
    }

    public RangeExpression Slice(Expression? start, Expression? end)
    {
        return new(HandBuiltModel.Node(), start, end, Range);
    }

    public IndexExpression Sliced(Expression receiver, RangeExpression range)
    {
        return new(HandBuiltModel.Node(), receiver, range, Sequence()) { ParameterType = Range };
    }

    public InvocationExpression Call(Expression value, IMistType target, Expression? receiver = null)
    {
        var method = new Method("Take", Int, [new Parameter("value", target)], new MemberModifiers());
        return new InvocationExpression(HandBuiltModel.Node(), receiver ?? Literal(0L), [value], Int, method, [target]);
    }

    public static IfStatement If(Expression condition, params Statement[] statements)
    {
        return new(condition, HandBuiltModel.Block(statements), null);
    }
}