using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The overload Mist bound, kept when the target would choose another. A body is emitted more than
/// once under a different <c>this</c> or type arguments -- an abstract's default restated into each
/// conformer, a base struct's members flattened into a derived one, a generic specialized per union
/// instantiation -- and Roslyn resolves a call written by name against the types it sees there, so
/// a <c>this</c>, type-parameter-typed, or callable argument to an overloaded member can pick an
/// overload the program never bound and the analysis never judged. Casting it to the selected
/// parameter's type pins the call; callable targets use the call's closed parameter type.
/// </summary>
internal static class OverloadPinning
{
    public static string? CastFor(Expression argument, Parameter parameter, Method? callee)
    {
        return CastFor(argument, parameter, parameter.Type, callee);
    }

    public static string? CastFor(Expression argument, Parameter parameter, IMistType closedTarget, Method? callee)
    {
        var target = argument is LambdaExpression or FunctionReferenceExpression ? closedTarget : parameter.Type;
        return callee is not null && !parameter.Modifier.IsRef ? CastFor(argument, target, callee is Operator || Overloaded(callee)) : null;
    }

    public static string? CastFor(Expression argument, IMistType target, bool overloaded)
    {
        if(!overloaded || UnknownUnions.Widens(argument.Type, target))
        {
            return null;
        }

        if(argument is LambdaExpression or FunctionReferenceExpression)
        {
            return argument is LambdaExpression { UnionCarrierDelegateType: not null }
                || DelegateShape.Of(target) is null
                || DelegateLowering.NeedsConstruction(argument.Type, target)
                    ? null
                    : TypeNameEmitter.Emit(target);
        }

        if(argument is not ThisExpression && argument.Type is not TypeParameter)
        {
            return null;
        }

        return target is not (UnknownType or MemberType or ConstructedType { Definition: MemberType }) || Mentions(target) ? null : TypeNameEmitter.Emit(target);
    }

    public static bool Overloaded(Indexer indexer)
    {
        var count = 0;

        for(var owner = indexer.DeclaringType; owner is not null; owner = owner is ConcreteType { BaseType: { } baseType } ? Definition(baseType) : null)
        {
            count += owner.IndexersList.Count(candidate => candidate.ParametersList.Count == indexer.ParametersList.Count);
        }

        return count > 1;
    }

    private static bool Overloaded(Method callee)
    {
        if(callee is Constructor { DeclaringType: ConcreteType built })
        {
            return built.ConstructorsList.Count(candidate => !candidate.Modifiers.IsStatic && candidate.ParametersList.Count == callee.ParametersList.Count) > 1;
        }

        var count = 0;

        for(var owner = callee.DeclaringType; owner is not null; owner = owner is ConcreteType { BaseType: { } baseType } ? Definition(baseType) : null)
        {
            count += owner.MethodsList.Count(method => method.Name == callee.Name && method.ParametersList.Count == callee.ParametersList.Count);
        }

        return count > 1;
    }

    private static MemberType? Definition(IMistType type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) as MemberType;
    }

    private static bool Mentions(IMistType type)
    {
        return type switch
        {
            TypeParameter => true,
            ConstructedType constructed => constructed.TypeArguments.Any(Mentions),
            _ => false,
        };
    }
}