using Mist.Backends.CSharpTranspiler.Emitter;
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
/// a <c>this</c> or a type-parameter-typed argument to an overloaded member can pick an overload the
/// program never bound and the analysis never judged. Casting it to the bound parameter's type pins
/// the call; everywhere else the cast is an identity.
/// </summary>
internal static class OverloadPinning
{
    public static string? CastFor(Expression argument, Parameter parameter, Method? callee)
    {
        return callee is not null && !parameter.Modifier.IsRef ? CastFor(argument, parameter.Type, callee is Operator || Overloaded(callee)) : null;
    }

    public static string? CastFor(Expression argument, IMistType target, bool overloaded)
    {
        if(!overloaded || (argument is not ThisExpression && argument.Type is not TypeParameter))
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