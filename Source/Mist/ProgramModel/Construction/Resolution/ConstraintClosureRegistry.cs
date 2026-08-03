using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// Every type used to close a PARAMETERIZED constructor constraint, paired with the signature it
/// closes and a node to report against.
/// <para>
/// SEPARATE FROM <see cref="TypeInterner"/> BECAUSE THE INTERNER CANNOT SEE HALF OF THEM. A generic
/// TYPE closure interns a ConstructedType, but a generic METHOD closure does not -- a call's type
/// arguments live on the expression that denotes the callee and are never interned, so
/// <c>Foo(7)</c> resolved to <c>Foo of Widget</c> would be invisible. Both kinds are recorded here
/// instead, so one collection answers the whole question.
/// </para>
/// <para>
/// A closure reached through substitution is deliberately NOT recorded. An <c>Outer of T</c> that
/// constructs an inner generic must itself carry the constraint for the program to type-check, so
/// the concrete type reaches the inner one only by closing the OUTER, and that closure is a source
/// site already recorded here.
/// </para>
/// </summary>
internal sealed class ConstraintClosureRegistry
{
    private readonly Dictionary<(IMistType, string), ConstraintClosure> closures = [];

    public IEnumerable<ConstraintClosure> Closures => closures.Values;

    /// <summary>
    /// Records the closures a constructed generic TYPE performs, one per parameterized constructor
    /// constraint on the parameter each argument fills.
    /// </summary>
    public void RecordType(IMistType definition, IReadOnlyList<IMistType> typeArguments, SyntaxNode node)
    {
        if(definition is not MemberType member)
        {
            return;
        }

        Record(member.TypeParametersList, typeArguments, node);
    }

    /// <summary>
    /// Records the closures a generic METHOD call performs.
    /// </summary>
    public void RecordMethod(Method method, IReadOnlyList<IMistType> typeArguments, SyntaxNode node)
    {
        Record(method.TypeParametersList, typeArguments, node);
    }

    private void Record(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> typeArguments, SyntaxNode node)
    {
        for(var i = 0; i < parameters.Count && i < typeArguments.Count; i++)
        {
            foreach(var signature in Parameterized(parameters[i]))
            {
                var argument = typeArguments[i];

                // An argument that is still a type parameter closes nothing -- it is the enclosing
                // declaration's own constraint being forwarded, and that declaration's closures are
                // recorded where IT is closed.
                if(argument is TypeParameter)
                {
                    continue;
                }

                _ = closures.TryAdd((argument, Key(signature)), new ConstraintClosure(argument, signature, node));
            }
        }
    }

    private static string Key(IReadOnlyList<IMistType> signature)
    {
        return string.Join(",", signature.Select(type => type.FullyQualifiedName));
    }

    public static List<IReadOnlyList<IMistType>> Parameterized(TypeParameter parameter)
    {
        var signatures = new List<IReadOnlyList<IMistType>>();

        foreach(var constraint in parameter.ConstraintsList)
        {
            if(constraint is ConstructorConstraint { ParameterTypes.Count: > 0 } constructorConstraint)
            {
                signatures.Add(constructorConstraint.ParameterTypes);
            }
        }

        return signatures;
    }
}

internal sealed record ConstraintClosure(
    IMistType TypeArgument, IReadOnlyList<IMistType> Signature, SyntaxNode Node);