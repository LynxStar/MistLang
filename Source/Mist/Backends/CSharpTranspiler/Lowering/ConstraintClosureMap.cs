using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
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
    private readonly Dictionary<MemberType, List<IReadOnlyList<IMistType>>> byDefinition = [];

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
                diagnostics.ReportAtNode(
                    BackendDiagnostics.ForeignConstructorConstraintClosure,
                    closure.Node,
                    null,
                    TypeDisplay.Mist(closure.TypeArgument),
                    string.Join(", ", closure.Signature.Select(TypeDisplay.Mist)));

                continue;
            }

            map.Add(definition, closure.Signature);
        }

        return map;
    }

    public IReadOnlyList<IReadOnlyList<IMistType>> For(MemberType type)
    {
        return byDefinition.TryGetValue(type, out var signatures) ? signatures : [];
    }

    private void Add(MemberType definition, IReadOnlyList<IMistType> signature)
    {
        if(!byDefinition.TryGetValue(definition, out var signatures))
        {
            signatures = [];
            byDefinition[definition] = signatures;
        }

        if(!signatures.Any(existing => Same(existing, signature)))
        {
            signatures.Add(signature);
        }
    }

    private static bool Same(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        return left.Count == right.Count
            && !left.Where((type, i) => !ReferenceEquals(type, right[i])).Any();
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