using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
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
    private readonly List<TypeArgumentClosing> closings = [];
    private readonly List<TypeArgumentClosing> openClosings = [];

    public IEnumerable<ConstraintClosure> Closures => closures.Values;

    /// <summary>
    /// Every type argument written or inferred in source against the parameter it closes, whatever
    /// that parameter's constraints. The constructor closures above are the subset emission needs a
    /// witness for; a rule judging the closing itself needs all of them, and the node each was closed at.
    /// </summary>
    public IReadOnlyList<TypeArgumentClosing> Closings => closings;

    /// <summary>
    /// The closings whose argument is itself a type parameter: an enclosing declaration forwarding its
    /// own, which a rule about what a closing lets through has to follow to that declaration's closings.
    /// </summary>
    public IReadOnlyList<TypeArgumentClosing> OpenClosings => openClosings;

    public IEnumerable<TypeArgumentClosing> StampableOpenClosings => openClosings.Where(closing =>
        closing.Parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable));

    /// <summary>
    /// Records the closures a constructed generic TYPE performs, one per parameterized constructor
    /// constraint on the parameter each argument fills.
    /// </summary>
    public void RecordType(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, SyntaxNode node, ConstructedType? enclosing = null)
    {
        if(definition is TypeAlias alias)
        {
            var aliasClosing = TypeSubstitution.ForReceiver(enclosing)
                .Extend(TypeSubstitution.ForParameters(alias.TypeParametersList, typeArguments));
            Record(alias.TypeParametersList, typeArguments, aliasClosing, node, stampableOnly: true);
            return;
        }

        if(definition is not MemberType member)
        {
            return;
        }

        var closing = TypeSubstitution.ForReceiver(enclosing)
            .Extend(TypeSubstitution.ForParameters(member.TypeParametersList, typeArguments));

        Record(member.TypeParametersList, typeArguments, closing, node);
    }

    public void RecordStampableType(
        IMistType definition, IReadOnlyList<IMistType> typeArguments, SyntaxNode node, ConstructedType? enclosing = null)
    {
        if(definition is MemberType member)
        {
            var closing = TypeSubstitution.ForReceiver(enclosing)
                .Extend(TypeSubstitution.ForParameters(member.TypeParametersList, typeArguments));
            Record(member.TypeParametersList, typeArguments, closing, node, stampableOnly: true);
        }
    }

    /// <summary>
    /// Records the closures a generic METHOD call performs.
    /// </summary>
    public void RecordMethod(
        Method method, IReadOnlyList<IMistType> typeArguments, TypeSubstitution closing, SyntaxNode node)
    {
        Record(method.TypeParametersList, typeArguments, method.BlockClosing.Extend(closing), node);
    }

    private void Record(
        IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> typeArguments, TypeSubstitution closing, SyntaxNode node,
        bool stampableOnly = false)
    {
        for(var i = 0; i < parameters.Count && i < typeArguments.Count; i++)
        {
            if(stampableOnly && !parameters[i].ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable))
            {
                continue;
            }

            if(typeArguments[i] is not TypeParameter)
            {
                closings.Add(new TypeArgumentClosing(parameters[i], typeArguments[i], node, closing));
            }
            else
            {
                openClosings.Add(new TypeArgumentClosing(parameters[i], typeArguments[i], node, closing));
            }

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

                var closed = signature.Select(type => closing.Apply(type, interner: null)).ToList();

                _ = closures.TryAdd(
                    (argument, $"{Key(signature)}|{Key(closed)}"), new ConstraintClosure(argument, signature, closed, node));
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
    IMistType TypeArgument, IReadOnlyList<IMistType> Signature, IReadOnlyList<IMistType> Closed, SyntaxNode Node);

/// <param name="Closing">
/// The whole site's substitution rather than this one pair, because a constraint may name a sibling
/// parameter, the receiver's or an enclosing declaration's -- <c>where U : T</c>, <c>where T : IRank of T</c> --
/// and is only judged once every parameter it names is replaced by what this site closed it with.
/// </param>
internal sealed record TypeArgumentClosing(TypeParameter Parameter, IMistType Argument, SyntaxNode Node, TypeSubstitution Closing);