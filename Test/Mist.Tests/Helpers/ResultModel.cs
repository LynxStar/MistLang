using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using static Mist.Tests.Helpers.HandBuiltModel;

namespace Mist.Tests.Helpers;

internal sealed class ResultModel
{
    public HandBuiltModel Built { get; } = new();
    public ClassType Definition { get; } = new("Result<,>", "Standard", null, null, null) { Modifiers = new MemberModifiers { IsSealed = true } };
    public IMistType Integer => Built.Type("Int32");
    public IMistType Text => Built.Type("String");
    public IMistType Boolean => Built.Type("Boolean");
    public IMistType Void => Built.Type("Void");
    public RefinedType True { get; }
    public RefinedType False { get; }

    public ResultModel()
    {
        True = new RefinedType(Boolean, Node(), "True") { Predicate = Tag(true) };
        False = new RefinedType(Boolean, Node(), "False") { Predicate = Tag(false) };
        var t = new TypeParameter("T", 0, Definition.Scope, Definition);
        var e = new TypeParameter("E", 1, Definition.Scope, Definition);
        Definition.TypeParametersList.AddRange([t, e]);
        var inspection = Inspection(t, e);
        Definition.MethodsList.Add(new Method("Inspect", inspection, [], default) { DeclaringType = Definition });
        Definition.ConstructorsList.Add(new Constructor("Result", Definition, [new Parameter("state", inspection)], default));
    }

    public ConstructedType Result(IMistType success, IMistType error)
    {
        return Built.Interner.GetOrCreateConstructedType(Definition, [success, error]);
    }

    public ConstructedType Projection(IMistType payload, bool success)
    {
        var definition = new ClassType(success ? "ResultSuccess<>" : "ResultFailure<>", "Standard", null, null, null);
        var parameter = new TypeParameter("T", 0, definition.Scope, definition);
        definition.TypeParametersList.Add(parameter);
        definition.FieldsList.Add(new Field("Payload", parameter, default, default));
        return Built.Interner.GetOrCreateConstructedType(definition, [payload]);
    }

    public UnionType Inspection(IMistType success, IMistType error)
    {
        return (UnionType)Built.Interner.GetOrCreateUnionType([
            Built.Interner.GetOrCreateTupleType([True, success]), Built.Interner.GetOrCreateTupleType([False, error])]);
    }

    public ComparisonPattern Tag(bool success)
    {
        return new ComparisonPattern(Node(), Boolean, ComparisonKind.Equal, Literal(Boolean, success));
    }

    public ResultPropagationExpression Propagate(Expression operand, IMistType? destination)
    {
        var shape = ResultTypes.Of(operand.Type)!;
        var inspection = shape.Inspection(Built.Interner);
        var success = inspection.Constituents.Single(part => ReferenceEquals(((ITupleType)part).ElementTypes[0], True));
        var failure = inspection.Constituents.Single(part => !ReferenceEquals(part, success));
        var error = new ElementReadExpression(Node(), shape.Error, operand);
        var target = ResultTypes.Of(destination);
        Expression returned = target is null ? error : new ResultConstructionExpression(Node(), error, target.Type, ResultCase.Failure);
        return new ResultPropagationExpression(Node(), operand, shape, inspection, success, failure, error, new ReturnStatement(returned, destination));
    }

    public InvocationExpression Inspect(Expression operand)
    {
        var type = (ConstructedType)operand.Type;
        var method = Definition.MethodsList.Single();
        return new InvocationExpression(Node(), new MemberAccessExpression(Node(), operand, "Inspect", method.ReturnType),
            [], ResultTypes.Of(type)!.Inspection(Built.Interner), method, []);
    }
}