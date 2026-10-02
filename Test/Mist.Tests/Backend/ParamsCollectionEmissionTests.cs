using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// Tier 3: the C# `params` modifier on a value-level params parameter (variadics Requirement 13.6),
// written for every collection the binder admits as it is for an array, with the collection spelled as
// the parameter's own type -- the optional over a reference kind bare, as every such optional is.
[Trait("Tier", "3")]
public sealed class ParamsCollectionEmissionTests
{
    private const string Types = """
        public class Takers
        {
            public static int Array(params int[] xs) { return xs.Length; }
            public static int Optional(params int[]? xs) { return 0; }
            public static int Listed(params List<int> xs) { return xs.Count; }
            public static int Sequence(params IEnumerable<int> xs) { return 0; }
            public static int Spanned(params ReadOnlySpan<int> xs) { return xs.Length; }
            public static int Headed(string head, params List<int> xs) { return xs.Count; }
        }
        """;

    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(() =>
    {
        var model = StandardModelProbe.Build("", out var diagnostics, Types);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return model;
    });

    [Theory]
    [InlineData("Array", "params int[] xs")]
    [InlineData("Optional", "params int[] xs")]
    [InlineData("Listed", "params System.Collections.Generic.List<int> xs")]
    [InlineData("Sequence", "params System.Collections.Generic.IEnumerable<int> xs")]
    [InlineData("Spanned", "params System.ReadOnlySpan<int> xs")]
    [InlineData("Headed", "string head, params System.Collections.Generic.List<int> xs")]
    public void AParamsCollection_IsDeclaredWithTheCSharpModifier(string method, string expected)
    {
        var takers = Model.Value.TypesByFQN.Values.OfType<MemberType>().Single(type => type.NamespaceName == "Probe" && type.Name == "Takers");
        var declared = takers.MethodsList.Single(candidate => candidate.Name == method);

        Assert.Equal(expected, ParameterEmitter.Emit(declared.ParametersList, new DiagnosticBag()));
    }
}