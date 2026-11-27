using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Model.Scopes;
using Mist.Tests.Generators;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class QualifiedUsingTriviaTests
{
    [Fact]
    public void SpacedSeparatorsRegisterTheFullDottedNameForLookupAndValidation()
    {
        var source = new SourceText("", "using A . B . C;");
        var directive = new UsingDeclarationNode
        {
            Name = new QualifiedIdentifierNode
            {
                IDENTIFIER = MistTypeGenerator.MakeToken("A", 6),
                IDENTIFIERs = [MistTypeGenerator.MakeToken("B", 10), MistTypeGenerator.MakeToken("C", 14)],
                Span = new Storm.Runtime.SourceSpan(6, 9),
            },
        };
        var ast = new CompilationUnitNode { Usings = [directive] };
        var model = new Model();
        var diagnostics = new DiagnosticBag();
        var context = new BuilderContext(model, diagnostics, NullAmbientTypeProvider.Instance);
        var assembly = model.AddSelfAssembly("Test");
        var file = new FileScope(context.ScopeFor(assembly), model, diagnostics);

        _ = new NamespaceBuilder(context).Build(file, ast, source, "Inferred", "/project");

        Assert.Equal("A.B.C", Assert.Single(file.Imports));
        var pending = Assert.Single(context.PendingUsings);
        Assert.Equal("A.B.C", pending.NamespaceName);
        Assert.Same(directive, pending.Directive);
    }
}