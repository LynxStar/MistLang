using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Model;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class GlobalUsingValidationTests
{
    [Fact]
    public void AnUndefinedNamespaceWarnsOnceAndIsWithdrawn()
    {
        var f = new Validation(["Known", "No.Such", "No.Such"]);
        _ = f.Model.GetOrAddNamespace("Known");

        var diagnostic = Assert.Single(f.Run());

        Assert.Equal("MIST_GLOBAL_USING_UNDEFINED_NAMESPACE", diagnostic.Id.Value);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("'No.Such'", diagnostic.Message);
        Assert.Contains("'Root'", diagnostic.Message);
        Assert.Equal<string>(["Known"], f.Self.GlobalUsings);
        Assert.Equal<string>(["Known"], f.Context.ScopeFor(f.Self).Imports);
    }

    [Theory]
    [InlineData("A.B.C")]
    [InlineData("A.B")]
    [InlineData("A")]
    public void ANamespaceAndEachPrefixOfItIsKept(string imported)
    {
        var f = new Validation([imported]);
        _ = f.Model.GetOrAddNamespace("A.B.C");

        Assert.Empty(f.Run());
        Assert.Equal<string>([imported], f.Self.GlobalUsings);
        Assert.Equal<string>([imported], f.Context.ScopeFor(f.Self).Imports);
    }

    [Fact]
    public void AReferencedProjectIsJudgedOnItsOwnUsingsAndNamedByItsManifest()
    {
        var f = new Validation(["Shared"]);
        var library = f.Model.AddReferenceAssembly("Lib", "/lib/mist.toml", ["Shared", "Lib.Missing"]);
        _ = f.Model.GetOrAddNamespace("Shared");

        var diagnostic = Assert.Single(f.Run());

        Assert.Equal("/lib/mist.toml", diagnostic.CompilationUnitPath);
        Assert.Contains("'Lib.Missing'", diagnostic.Message);
        Assert.Contains("'Lib'", diagnostic.Message);
        Assert.Equal<string>(["Shared"], library.GlobalUsings);
        Assert.Equal<string>(["Shared"], f.Self.GlobalUsings);
    }

    private sealed class Validation
    {
        private readonly DiagnosticBag diagnostics = new();

        public Validation(ImmutableArray<string> globalUsings)
        {
            Self = Model.AddSelfAssembly("Root", globalUsings);
            Context = new BuilderContext(Model, diagnostics, NullAmbientTypeProvider.Instance);
        }

        public Model Model { get; } = new();

        public MistAssembly Self { get; }

        public BuilderContext Context { get; }

        public ImmutableArray<Diagnostic> Run()
        {
            GlobalUsingValidation.Run(Context);
            return diagnostics.ToImmutableArray();
        }
    }
}