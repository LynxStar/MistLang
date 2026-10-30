using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which parameterized constructor constraints each declaration must satisfy, so a type can be
/// emitted carrying the interfaces and factories its uses demand.
/// <para>
/// Keyed on the DEFINITION rather than the closure. <c>Box of Widget</c> and <c>Box of Box of
/// Widget</c> both close through the one declaration of <c>Box</c>, whose own factory is written in
/// terms of its open parameter, so both are satisfied by a single append. That is also why no
/// instantiation-depth guard is needed here: the interface goes on a declaration, and there are
/// finitely many of those however many closures reach them.
/// </para>
/// </summary>
internal sealed class ConstraintClosureMap
{
    private readonly Dictionary<MemberType, List<ConstructorWitness>> byDefinition = [];

    public static ConstraintClosureMap Build(ProgramModel.Model.ProgramModel model, DiagnosticBag diagnostics)
    {
        var map = new ConstraintClosureMap();

        foreach(var closure in model.ConstructorConstraintClosures.Closures)
        {
            var definition = Definition(closure.TypeArgument);

            // Only a Mist declaration can be given an interface it did not ask for. Reported here
            // because this is the point where the backend learns it cannot express the program.
            if(definition is null || !definition.Assembly.HasMistSource)
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.ForeignConstructorConstraintClosure,
                    closure.Node,
                    null,
                    DiagnosticSnapshot.Type(closure.TypeArgument),
                    new SequenceValue(SlotKind.Type, [.. closure.Closed.Select(DiagnosticSnapshot.Type)]));

                continue;
            }

            map.Add(definition, new ConstructorWitness(closure.Signature, closure.Closed, Declared(definition, closure.TypeArgument, closure.Closed)));
        }

        return map;
    }

    public IReadOnlyList<ConstructorWitness> For(MemberType type)
    {
        return byDefinition.TryGetValue(type, out var witnesses) ? witnesses : [];
    }

    private void Add(MemberType definition, ConstructorWitness witness)
    {
        if(!byDefinition.TryGetValue(definition, out var witnesses))
        {
            witnesses = [];
            byDefinition[definition] = witnesses;
        }

        if(!witnesses.Any(existing => Same(existing.Factory, witness.Factory)
            && ConstructorConstraintInterfaces.InterfaceName(existing.Signature) == ConstructorConstraintInterfaces.InterfaceName(witness.Signature)))
        {
            witnesses.Add(witness);
        }
    }

    public static bool Same(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count
            && !left.Where((type, i) => type.FullyQualifiedName != right[i].FullyQualifiedName).Any();
    }

    /// <summary>
    /// The parameter types of the constructor a closure selects, as its own declaration writes them: a
    /// constructed generic closes through its definition, whose factory can name only the definition's
    /// parameters. Selected as the constraint rule admits one, by identity once the closure substitutes
    /// them; the closed signature where no constructor is written.
    /// </summary>
    private static IReadOnlyList<IMistType> Declared(MemberType definition, IMistType typeArgument, IReadOnlyList<IMistType> closed)
    {
        var substitution = TypeSubstitution.ForReceiver(typeArgument as ConstructedType);

        return (definition as ConcreteType)?.ConstructorsList.Find(constructor =>
            !constructor.Modifiers.IsStatic
            && constructor.Modifiers.Accessibility == Accessibility.Public
            && constructor.ParametersList.Count == closed.Count
            && constructor.ParametersList.Select((parameter, i) =>
                ConversionRelation.IsIdentity(substitution.Apply(parameter.Type, interner: null), closed[i])).All(same => same))
            ?.ParametersList.ConvertAll(parameter => parameter.Type) ?? closed;
    }

    /// <summary>
    /// The declaration a closure lands on. A constructed generic closes through its own definition,
    /// since that is the only thing with a base list to extend.
    /// </summary>
    private static MemberType? Definition(IMistType typeArgument)
    {
        return typeArgument switch
        {
            ConstructedType constructed => constructed.Definition as MemberType,
            MemberType member => member,
            _ => null,
        };
    }
}

/// <param name="Signature">The declaration's, which names the interface.</param>
/// <param name="Closed">The same signature as a closing use fills it.</param>
internal sealed record ConstructorWitness(IReadOnlyList<IMistType> Signature, IReadOnlyList<IMistType> Closed, IReadOnlyList<IMistType> Declared)
{
    public IReadOnlyList<IMistType> Factory { get; } = [.. Signature.Select((type, i) => ConstructorConstraintInterfaces.IsOpen(type) ? Declared[i] : Closed[i])];
}