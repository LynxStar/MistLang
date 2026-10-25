using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Model;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class UsingValidationTests
{
    [Fact]
    public void AnUndefinedGlobalUsingWarnsOnceAndIsWithdrawn()
    {
        var f = new Validation(["Known", "No.Such", "No.Such"]);
        _ = f.Model.GetOrAddNamespace("Known");

        var diagnostic = Assert.Single(f.Run());

        Assert.Equal("MIST_GLOBAL_USING_UNDEFINED_NAMESPACE", diagnostic.Number.Value);
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
        f.Using(imported);

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

        Assert.Equal("/lib/mist.toml", diagnostic.Location.Path);
        Assert.Contains("'Lib.Missing'", diagnostic.Message);
        Assert.Contains("'Lib'", diagnostic.Message);
        Assert.Equal<string>(["Shared"], library.GlobalUsings);
        Assert.Equal<string>(["Shared"], f.Self.GlobalUsings);
    }

    [Fact]
    public void EachDirectiveNamingAnUndefinedNamespaceIsAnError()
    {
        var f = new Validation([]);
        _ = f.Model.GetOrAddNamespace("Known");
        f.Using("Known");
        f.Using("No.Such");
        f.Using("Known.Not");

        var diagnostics = f.Run();

        Assert.All(diagnostics, d => Assert.Equal(("MIST_UNDEFINED_NAMESPACE", DiagnosticSeverity.Error), (d.Number.Value, d.Severity)));
        Assert.Equal(
            ["The namespace 'No.Such' could not be found.", "The namespace 'Known.Not' could not be found."],
            diagnostics.Select(d => d.Message));
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

        public void Using(string namespaceName)
        {
            var parts = namespaceName.Split('.');
            var directive = new UsingDeclarationNode
            {
                Name = new QualifiedIdentifierNode
                {
                    IDENTIFIER = MistTypeGenerator.MakeToken(parts[0]),
                    IDENTIFIERs = [.. parts.Skip(1).Select(part => MistTypeGenerator.MakeToken(part))],
                },
            };

            Context.PendingUsings.Add(new PendingUsing(directive, namespaceName));
        }

        public ImmutableArray<Diagnostic> Run()
        {
            UsingValidation.Run(Context);
            return diagnostics.ToImmutableArray();
        }
    }
}