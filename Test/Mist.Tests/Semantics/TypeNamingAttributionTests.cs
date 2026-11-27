using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class TypeNamingAttributionTests
{
    [Fact]
    public void NestedTypeNamingRecordsTheInnermostOwnerAndItsAssembly()
    {
        var model = new Model();
        var assembly = model.AddSelfAssembly("Consumer");
        var assemblyScope = new AssemblyScope(new ProgramScope(model), assembly, model, new DiagnosticBag());
        var outer = new ClassType("Outer", "Consumer", assemblyScope, null, null) { Assembly = assembly };
        var inner = new ClassType("Inner", "Consumer", outer.Scope, outer, null) { Assembly = assembly };
        var library = new MistAssembly("Library", MistAssemblyKind.Reference);
        var named = new ClassType("Dependency", "Library", null, null, null) { Assembly = library };
        var node = new QualifiedIdentifierNode();
        var registry = new TypeNamingRegistry();

        registry.Record(named, inner.Scope, node);

        var naming = Assert.Single(registry.Namings);
        Assert.Same(named, naming.Named);
        Assert.Same(node, naming.Node);
        Assert.Same(inner, naming.Owner);
        Assert.Same(assembly, naming.Assembly);
    }

    [Fact]
    public void RebindingUnderACloneScopeLeavesOnlyTheSourceNaming()
    {
        var lexicalScope = new Scope(null);
        var source = new ClassType("Source", "Consumer", lexicalScope, null, null);
        var cloneScope = new CloneScope(lexicalScope, source, new Scope(null));
        var clone = new ClassType("Clone", "Consumer", cloneScope, null, null);
        var node = new QualifiedIdentifierNode();
        var registry = new TypeNamingRegistry();
        registry.Record(source, new Scope(source.Scope), node);
        var original = Assert.Single(registry.Namings);

        registry.Record(clone, new Scope(clone.Scope), node);

        Assert.Same(original, Assert.Single(registry.Namings));
    }
}