using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.RuntimeDependentValues;

[Trait("Tier", "3")]
public sealed class SlotReadBindingTests : ProgramModelProbe
{
    public SlotReadBindingTests()
        : base("MistSlotReads_")
    {
    }

    protected override string Fixtures { get; } = """
        public class Ring<Capacity:int>
        {
        }

        public class Bounded<Capacity:int> where Capacity >= 1
        {
        }

        public abstract ISized<N:int>
        {
        }

        public class Both : ISized<1>, ISized<2>
        {
        }

        public class Api
        {
            public static Ring<N> Zeroes<N:int>() { return new Ring<N>(); }
            public static int Size<N:int>(Ring<N> r) { return 0; }
        }
        """;

    protected override string Preamble { get; } = "";

    [Fact]
    public void AnInstanceReadBindsAsASlotReadAndTheReadThroughTheTypeIsUnchanged()
    {
        var statements = BodyIn("var ring = new Ring<4>(); var c = ring.Capacity; var s = Ring<4>.Capacity;")
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var instance = Assert.IsType<MemberAccessExpression>(statements[1].Initializer);
        var through = Assert.IsType<MemberAccessExpression>(statements[2].Initializer);

        Assert.Equal("Capacity", Assert.IsType<TypeParameter>(Assert.IsType<TypeResult>(instance.Member).Type).Name);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, instance.Type.FullyQualifiedName);
        Assert.Equal(4L, Assert.IsType<LiftedType>(Assert.IsType<ConstructedType>(instance.Receiver.Type).TypeArguments[0]).Value);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, Assert.IsType<PropertyResult>(through.Member).Property.Type.FullyQualifiedName);
        Assert.Equal("Capacity", Assert.IsType<PropertyResult>(through.Member).Property.ProjectedParameter?.Name);
    }

    [Fact]
    public void ADiagnosticSpellsASlotReadByTheSlotsDomain()
    {
        var diagnostics = BuildRun(
            "var n = 3; var ring = new Ring<n>(); var four = new Ring<4>(); var a = ring.Capacity(); var b = four.Capacity[0]; var (p, q) = ring.Capacity; var e = four.Capacity.Nope; var f = (ring.Capacity, 1).Nope;");
        var members = Errors(diagnostics, "MIST0301").Select(diagnostic => diagnostic.Message).ToList();

        Assert.Contains("its type 'int' is not a function type", Assert.Single(Errors(diagnostics, "MIST0363")).Message, StringComparison.Ordinal);
        Assert.StartsWith("'int' cannot be indexed", Assert.Single(Errors(diagnostics, "MIST0354")).Message, StringComparison.Ordinal);
        Assert.StartsWith("'Int32' is not a tuple", Assert.Single(Errors(diagnostics, "MIST0366")).Message, StringComparison.Ordinal);
        Assert.Equal(2, members.Count);
        Assert.Contains(members, message => message.StartsWith("'int' has no member named 'Nope'", StringComparison.Ordinal));
        Assert.Contains(members, message => message.StartsWith("'(int, int)' has no member named 'Nope'", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSlotOfAnAbstractIsRefusedThroughUnknownAndTwoClosingsOfOneAbstractBuild()
    {
        var both = BuildRun("var both = new Both(); ISized<1> one = both; var n = one.N;");
        var some = BuildRun("ISized<unknown> some = new Both(); var n = some.N;");

        Assert.DoesNotContain(both, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.StartsWith("'N' cannot be read through 'ISized<unknown>'", Assert.Single(Errors(some, "MIST0489")).Message, StringComparison.Ordinal);
        Assert.Empty(Errors(some, "MIST0301"));
    }

    [Fact]
    public void AStatedArgumentIsACaptureOnTheCallAndTheResultTypeSharesIt()
    {
        var declaration = DeclarationIn("var count = 3; var z = Api.Zeroes<count>();", "");
        var call = Assert.IsType<InvocationExpression>(declaration.Initializer);
        var capture = Assert.Single(call.Captures);

        Assert.Equal("count", Assert.IsType<LocalReferenceExpression>(capture.Initializer).Local.Name);
        Assert.Same(capture.Value, Assert.IsType<ConstructedType>(declaration.Local.Type).TypeArguments[0]);
        Assert.Same(capture.Value, Assert.IsType<MemberAccessExpression>(call.Receiver).TypeArguments[0]);
    }

    [Fact]
    public void AnInferredParameterOverAClassClosedWithUnknownReadsTheSlotThroughItsDomain()
    {
        var model = BuildModel("Ring<unknown> r = new Ring<4>(); var s = Api.Size(r);", "", "", out var diagnostics);
        var statements = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Probe"]).MethodsList.Single(method => method.Name == "Run").Body!
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var call = Assert.IsType<InvocationExpression>(statements[1].Initializer);
        var capture = Assert.Single(call.Captures);
        var read = Assert.IsType<MemberAccessExpression>(capture.Initializer);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Equal("r.Capacity", capture.Name);
        Assert.Equal("r", Assert.IsType<LocalReferenceExpression>(read.Receiver).Local.Name);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, read.Type.FullyQualifiedName);
        Assert.Same(capture.Value, Assert.IsType<ConstructedType>(call.ParameterTypes![0]).TypeArguments[0]);
    }

    [Fact]
    public void ASignatureTypeNamingARegularParameterIsRefused()
    {
        var diagnostics = BuildRun("var x = 1;", "public class Bad { public static Ring<n> Make(int n) { return new Ring<4>(); } }");

        Assert.Contains("cannot name the value 'n'", Assert.Single(Errors(diagnostics, "MIST0487")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACastAnAsAndAReadThroughAClosingOwnTheirCaptures()
    {
        var statements = BodyIn("var n = 4; Ring<4> four = new Ring<4>(); unknown thing = four; var cast = (Ring<n>)four; var tested = thing as Ring<n>; var read = Ring<n>.Capacity;")
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var cast = Assert.IsType<CastExpression>(statements[3].Initializer);
        var tested = Assert.IsType<AsExpression>(statements[4].Initializer);
        var read = Assert.IsType<MemberAccessExpression>(statements[5].Initializer);

        Assert.Same(Assert.Single(cast.Captures).Value, Assert.IsType<ConstructedType>(cast.TargetType).TypeArguments[0]);
        Assert.Same(Assert.Single(tested.Captures).Value, Assert.IsType<ConstructedType>(tested.TargetType).TypeArguments[0]);
        Assert.Same(Assert.Single(read.Captures).Value, Assert.IsType<ConstructedType>(Assert.IsType<TypeReferenceExpression>(read.Receiver).ReferencedType).TypeArguments[0]);
        _ = Assert.IsType<PropertyResult>(read.Member);
    }

    [Fact]
    public void AReadOfASlotDeclaredWithAWhereIsTypedByTheDomainAlone()
    {
        var statements = BodyIn("var b = new Bounded<4>(); var c = b.Capacity; c = 0;")
            .Statements.OfType<LocalDeclarationStatement>().ToList();
        var read = Assert.IsType<MemberAccessExpression>(statements[1].Initializer);

        Assert.Equal("Capacity", Assert.IsType<TypeParameter>(Assert.IsType<TypeResult>(read.Member).Type).Name);
        Assert.IsNotType<RefinedType>(read.Type);
        Assert.IsNotType<RefinedType>(statements[1].Local.Type);
        Assert.Equal(WellKnownTypes.Int32.FullyQualifiedName, statements[1].Local.Type.FullyQualifiedName);
    }
}