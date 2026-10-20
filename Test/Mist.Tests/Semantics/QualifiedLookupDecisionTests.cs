using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

/// <summary>
/// One qualification step off a namespace: a CLR type with a Standard intrinsic is that intrinsic, an
/// ordinary CLR type is its projection, and a name that is neither is a miss for the caller to refuse.
/// </summary>
[Trait("Tier", "1")]
public sealed class QualifiedLookupDecisionTests
{
    private readonly ProgramModelClass model = new();
    private readonly StubProvider provider = new();

    public QualifiedLookupDecisionTests()
    {
        provider.BuildAndRegister(model);
    }

    [Fact]
    public void AnIntrinsicClrNameIsTheStandardType()
    {
        var found = Lookup("System", "Int32");

        Assert.Same(provider.Int32, Assert.IsType<TypeResult>(found).Type);
    }

    [Fact]
    public void AnOrdinaryClrTypeIsItsProjection()
    {
        var found = Lookup("System", "Math");

        Assert.Same(provider.Math, Assert.IsType<TypeResult>(found).Type);
    }

    [Fact]
    public void ANameThatIsNeitherTypeNorNamespaceIsAMiss()
    {
        _ = Assert.IsType<NotFoundResult>(Lookup("System", "NoSuchType"));
    }

    [Fact]
    public void AnIntrinsicClrNameIsNotReachedThroughAnImportedNamespace()
    {
        Assert.Null(model.FindType("System", "Int32"));
    }

    private LookupResult Lookup(string namespaceName, string memberName)
    {
        return new BuilderContext(model, new DiagnosticBag(), provider).Resolver.LookupQualified(namespaceName, memberName);
    }

    private sealed class StubProvider : IAmbientTypeProvider
    {
        public ClassType Int32 { get; } = new("Int32", "Standard", new Scope(null), null, null);

        public ClassType Math { get; } = new("Math", "System", new Scope(null), null, null);

        public void BuildAndRegister(ProgramModelClass programModel)
        {
            _ = programModel.RegisterType(Int32);
            _ = programModel.GetOrAddNamespace("System");
            _ = programModel.RegisterType(Math);
            programModel.RegisterClrName("System.Int32", Int32);
        }
    }
}