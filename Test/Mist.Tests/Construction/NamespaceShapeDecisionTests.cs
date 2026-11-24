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

[Trait("Tier", "1")]
public sealed class NamespaceShapeDecisionTests
{
    private static readonly ParsedOrigin Source = new(new SourceText("shapes.mist", new string(' ', 200)));

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryFileScopedNamespaceAfterTheFirstIsRefusedAndTheFileIsDropped(int count)
    {
        var f = new Shapes();
        var items = new List<TopLevelItemNode>();
        for(var i = 0; i < count; i++)
        {
            items.Add(Top(FileScoped($"N{i}", At(i * 40))));
            items.Add(Using());
        }

        var fileNamespace = f.Build([.. items]);

        Assert.Null(fileNamespace);
        Assert.Equal(Enumerable.Range(1, count - 1).Select(i => ("MIST0290", i * 40)), f.Findings());
        Assert.All(f.Diagnostics.Admitted, finding => Assert.Equal(DiagnosticSeverity.Error, finding.Severity));
        Assert.Empty(f.Model.Namespaces);
    }

    [Fact]
    public void AFileScopedNamespaceAfterABlockIsRefusedAtTheFileScopedDeclaration()
    {
        var f = new Shapes();

        var fileNamespace = f.Build(Top(Block("First", At(0))), Top(FileScoped("Second", At(40))));

        Assert.Null(fileNamespace);
        Assert.Equal([("MIST0291", 40)], f.Findings());
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(f.Diagnostics.Admitted).Severity);
        Assert.Empty(f.Model.Namespaces);
    }

    [Fact]
    public void ABlockAfterAFileScopedNamespaceIsRefusedAtTheBlock()
    {
        var f = new Shapes();

        var fileNamespace = f.Build(Top(FileScoped("First", At(0))), Using(), Top(Block("Second", At(40))));

        Assert.Null(fileNamespace);
        Assert.Equal([("MIST0291", 40)], f.Findings());
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(f.Diagnostics.Admitted).Severity);
        Assert.Empty(f.Model.Namespaces);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AFileScopedNamespaceAtAnyDepthInsideABlockIsRefusedWhereItIsWritten(int depth)
    {
        var f = new Shapes();
        var block = Block($"B{depth}", At(depth * 20), Nested(FileScoped("Inner", At(150))));
        for(var level = depth - 1; level >= 1; level--)
        {
            block = Block($"B{level}", At(level * 20), Nested(block));
        }

        _ = f.Build(Top(block));

        Assert.Equal([("MIST0292", 150)], f.Findings());
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(f.Diagnostics.Admitted).Severity);
    }

    [Fact]
    public void AFileScopedNamespaceInsideABlockOfAFileScopedFileIsRefusedButTheFileKeepsItsNamespace()
    {
        var f = new Shapes();

        var fileNamespace = f.Build(Top(FileScoped("A", At(0), Nested(Block("B", At(20), Nested(FileScoped("C", At(150))))))));

        Assert.Equal([("MIST0292", 150)], f.Findings());
        Assert.Equal(DiagnosticSeverity.Error, Assert.Single(f.Diagnostics.Admitted).Severity);
        Assert.Equal("A", fileNamespace);
        Assert.Contains("A.B", f.Model.Namespaces.Keys);
    }

    [Fact]
    public void OneFileScopedNamespaceFollowedByOtherItemsIsTheFilesNamespace()
    {
        var f = new Shapes();

        var fileNamespace = f.Build(Using(), Top(FileScoped("Only", At(0), Nested(Block("Inner", At(20))))), Using());

        Assert.Equal("Only", fileNamespace);
        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Equal(["Only.Inner"], f.Model.Namespaces.Keys);
    }

    [Fact]
    public void SeveralBlocksWithNestedBlocksLeaveTheInferredNamespace()
    {
        var f = new Shapes();

        var fileNamespace = f.Build(
            Top(Block("A", At(0), Nested(Block("B", At(20))))), Using(), Top(Block("A", At(40))), Top(Block("C", At(60))));

        Assert.Equal("Inferred", fileNamespace);
        Assert.Empty(f.Diagnostics.Admitted);
        Assert.Equal(["A", "A.B", "C"], f.Model.Namespaces.Keys.Order());
    }

    private static Storm.Runtime.SourceSpan At(int offset)
    {
        return new Storm.Runtime.SourceSpan(offset, 10);
    }

    private static NamespaceItemTopLevelItemNode Top(NamespaceDeclarationNode declaration)
    {
        return new NamespaceItemTopLevelItemNode { NamespaceDeclaration = declaration };
    }

    private static UsingItemTopLevelItemNode Using()
    {
        return new UsingItemTopLevelItemNode { UsingDeclaration = new UsingDeclarationNode { Name = Name("System") } };
    }

    private static NamespaceNamespaceItemNamespaceBodyItemNode Nested(NamespaceDeclarationNode declaration)
    {
        return new NamespaceNamespaceItemNamespaceBodyItemNode { NamespaceDeclaration = declaration };
    }

    private static FileScopedNamespaceNamespaceDeclarationNode FileScoped(string name, Storm.Runtime.SourceSpan span, params NamespaceBodyItemNode[] body)
    {
        return new FileScopedNamespaceNamespaceDeclarationNode { Name = Name(name), Body = [.. body], Span = span, Origin = Source };
    }

    private static BlockNamespaceNamespaceDeclarationNode Block(string name, Storm.Runtime.SourceSpan span, params NamespaceBodyItemNode[] body)
    {
        return new BlockNamespaceNamespaceDeclarationNode { Name = Name(name), Body = [.. body], Span = span, Origin = Source };
    }

    private static QualifiedIdentifierNode Name(string name)
    {
        return new QualifiedIdentifierNode { IDENTIFIER = MistTypeGenerator.MakeToken(name), IDENTIFIERs = [] };
    }

    private sealed class Shapes
    {
        public Model Model { get; } = new();
        public DiagnosticBag Diagnostics { get; } = new();
        private readonly BuilderContext context;

        public Shapes()
        {
            context = new BuilderContext(Model, Diagnostics, NullAmbientTypeProvider.Instance);
            _ = Model.AddSelfAssembly("Test");
        }

        public string? Build(params TopLevelItemNode[] items)
        {
            var file = new FileScope(context.ScopeFor(Model.SelfAssembly!), Model, Diagnostics);
            return new NamespaceBuilder(context).Build(
                file, new CompilationUnitNode { Items = [.. items] }, new SourceText("", ""), "Inferred", "/project");
        }

        public (string, int)[] Findings()
        {
            return [.. Diagnostics.Admitted.Select(finding => (finding.Number.Value, Assert.IsType<SourceRange>(finding.Location).Offset))];
        }
    }
}