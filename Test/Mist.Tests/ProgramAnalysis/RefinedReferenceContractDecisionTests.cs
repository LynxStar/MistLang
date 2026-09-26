// Tier 3: reference contracts preserve the declared storage refinement for both reading and writing.

namespace Mist.Tests.ProgramAnalysis;

public sealed class RefinedReferenceContractDecisionTests
{
    private const string Types = """
        type Positive = int > 0;
        type NonNegative = int >= 0;
        type Strong = int >= 2;
        """;

    [Theory]
    [InlineData("public abstract IFactory { ref Positive Make(); } public class Factory : IFactory { private Positive held = 1; public ref Positive Make() { return ref held; } }")]
    [InlineData("public abstract IFactory<T> { ref T Make(); } public class Base<T> { protected T held; public ref T Make() { return ref held; } } public class Factory : Base<Positive>, IFactory<Positive> { }")]
    [InlineData("public abstract IWriter { void Fill(ref Positive value); } public class Writer : IWriter { public void Fill(ref Positive value) { value = 1; } }")]
    [InlineData("public abstract IWriter<T> { void Fill(ref T value); } public class Base<T> { public void Fill(ref T value) { } } public class Writer : Base<Positive>, IWriter<Positive> { }")]
    public void AReferenceContractWithTheSameDeclaredRefinement_IsAdmitted(string declarations)
    {
        Assert.Empty(RefinedReturnContractDecisionTests.Analyse(Types + declarations));
    }

    [Theory]
    [InlineData("public abstract IFactory { ref Positive Make(); } public class Factory : IFactory { private Strong held = 2; public ref Strong Make() { return ref held; } }")]
    [InlineData("public abstract IFactory { ref Positive Make(); } public class Factory : IFactory { private NonNegative held = 0; public ref NonNegative Make() { return ref held; } }")]
    [InlineData("public abstract IFactory { ref Positive Make(); } public class Factory : IFactory { private int held = 1; public ref int Make() { return ref held; } }")]
    [InlineData("public abstract IFactory<T> { ref T Make(); } public class Base<T> { protected T held; public ref T Make() { return ref held; } } public class Factory : Base<Strong>, IFactory<Positive> { }")]
    public void AReferenceReturnWithADifferentStorageRefinement_IsRefused(string declarations)
    {
        Assert.Equal("MIST_PA_REFINEMENT_RETURN_UNPROVEN", Assert.Single(RefinedReturnContractDecisionTests.Analyse(Types + declarations)).Id.Value);
    }

    [Theory]
    [InlineData("public abstract IWriter { void Fill(ref Positive value); } public class Writer : IWriter { public void Fill(ref Strong value) { value = 2; } }")]
    [InlineData("public abstract IWriter { void Fill(ref Positive value); } public class Writer : IWriter { public void Fill(ref NonNegative value) { value = 0; } }")]
    [InlineData("public abstract IWriter { void Fill(ref Positive value); } public class Writer : IWriter { public void Fill(ref int value) { value = -1; } }")]
    [InlineData("public abstract IWriter<T> { void Fill(ref T value); } public class Base<T> { public void Fill(ref T value) { } } public class Writer : Base<NonNegative>, IWriter<Positive> { }")]
    public void AReferenceParameterWithADifferentStorageRefinement_IsRefused(string declarations)
    {
        Assert.Equal("MIST_PA_CONSTRAINT_STRENGTHENED", Assert.Single(RefinedReturnContractDecisionTests.Analyse(Types + declarations)).Id.Value);
    }
}
