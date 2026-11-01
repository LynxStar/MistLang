using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class VoidPatterns
{
    public static bool IsUnitPattern(Pattern pattern)
    {
        return pattern switch
        {
            TypeTestPattern tested => IsUnitTest(tested),
            NegatedPattern negated => IsUnitPattern(negated.Operand),
            ConjunctionPattern conjunction => conjunction.Operands.Any(IsUnitPattern),
            DisjunctionPattern disjunction => disjunction.Operands.Any(IsUnitPattern),
            PropertyPattern { TypeTest: { } tested } => IsUnitTest(tested),
            _ => false,
        };
    }

    // C# rejects negated patterns that cannot match a struct; Boolean guards keep that test and its operand evaluation.
    public static bool IsUnitTest(TypeTestPattern pattern)
    {
        return pattern is { TestsAbsence: false, Constituent: null, Binding: null, Refinement: null }
            && OptionalUnion.IsVoid(pattern.NarrowedType)
            && OptionalUnion.IsVoid(Unwrapped(pattern.ScrutineeType));
    }

    private static IMistType Unwrapped(IMistType type)
    {
        return TypeNameEmitter.Closed(type) switch
        {
            TypeAlias { AliasedType: { } aliased } => Unwrapped(aliased),
            RefinedType refined => Unwrapped(refined.BaseType),
            var closed => closed,
        };
    }
}