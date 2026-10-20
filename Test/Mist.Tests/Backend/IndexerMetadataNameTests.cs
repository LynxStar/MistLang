using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class IndexerMetadataNameTests
{
    private readonly HandBuiltModel model = new();

    [Theory]
    [Trait("Tier", "1")]
    [InlineData("field")]
    [InlineData("property")]
    [InlineData("method")]
    [InlineData("nested")]
    public void DeclaredItemSelectsAnotherName(string kind)
    {
        var type = HandBuiltModel.Class("Shelf");
        AddMember(type, kind, "Item");

        Assert.Equal("Indexer", IndexerMetadataName.Of(type));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstructedBaseDeclarationsReserveNames(bool ambient)
    {
        var ancestor = HandBuiltModel.Class("Base<>");
        ancestor.TypeParametersList.Add(new TypeParameter("T", 0, ancestor.Scope, ancestor));
        if(ambient)
        {
            ancestor.Assembly = MistAssembly.Ambient;
        }

        AddMember(ancestor, "field", "Item");
        AddMember(ancestor, "method", "Indexer");
        var middle = HandBuiltModel.Class("Middle", new ConstructedType(ancestor, [model.Type("Int32")]));
        var type = HandBuiltModel.Class("Shelf", middle);

        Assert.Equal("Indexer1", IndexerMetadataName.Of(type));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void CandidatesSkipEveryDeclarationKindAndUnprojectedNames()
    {
        var type = HandBuiltModel.Class("Shelf");
        AddMember(type, "field", "Item");
        AddMember(type, "property", "Indexer");
        AddMember(type, "method", "Indexer1");
        AddMember(type, "nested", "Indexer2");
        _ = type.UnprojectedMemberNames.Add("Indexer3");

        Assert.Equal("Indexer4", IndexerMetadataName.Of(type));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NoCollisionKeepsItemRegardlessOfOverloads()
    {
        var type = HandBuiltModel.Class("Shelf");
        AddMember(type, "field", "item");
        _ = AddIndexer(type, "Int32");
        _ = AddIndexer(type, "String");

        Assert.Equal("Item", IndexerMetadataName.Of(type));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void CollidingOverloadsShareTheTypeDecision()
    {
        var type = HandBuiltModel.Class("Shelf");
        AddMember(type, "method", "Item");
        var first = AddIndexer(type, "Int32");
        var second = AddIndexer(type, "String");

        Assert.Equal("Indexer", IndexerMetadataName.Of(first.DeclaringType));
        Assert.Equal("Indexer", IndexerMetadataName.Of(second.DeclaringType));
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TypeEmissionNamesEveryOverloadAndUnsafeAccessorConsistently()
    {
        var type = HandBuiltModel.Class("Shelf");
        AddMember(type, "field", "Item");
        AddMember(type, "method", "Indexer");
        var first = AddIndexer(type, "Int32");
        var second = AddIndexer(type, "String");
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();
        var anyEmitted = false;

        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model.Model, diagnostics), model.Model.VariadicUses,
            writer, diagnostics, ref anyEmitted);
        var accessors = new UnsafeAccessors(type, "probe", diagnostics);
        _ = accessors.Indexer(first, type, isSetter: false);
        _ = accessors.Indexer(first, type, isSetter: true);
        _ = accessors.Indexer(second, type, isSetter: false);

        var text = writer.ToString();
        Assert.Equal(2, text.Split("[System.Runtime.CompilerServices.IndexerName(\"Indexer1\")]").Length - 1);
        Assert.Contains("this[int index]", text, StringComparison.Ordinal);
        Assert.Contains("this[string index]", text, StringComparison.Ordinal);
        var declarations = string.Join("\n", accessors.Declarations.SelectMany(lines => lines));
        Assert.Contains("Name = \"get_Indexer1\"", declarations, StringComparison.Ordinal);
        Assert.Contains("Name = \"set_Indexer1\"", declarations, StringComparison.Ordinal);
        Assert.DoesNotContain("get_Item", declarations, StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "2")]
    public void NoCollisionPreservesTheUnattributedDeclarationAndAccessor()
    {
        var type = HandBuiltModel.Class("Shelf");
        var indexer = AddIndexer(type, "Int32");
        var diagnostics = new DiagnosticBag();
        var writer = new CSharpWriter();

        IndexerEmitter.Emit(indexer, false, false, writer, diagnostics);
        var accessors = new UnsafeAccessors(type, "probe", diagnostics);
        _ = accessors.Indexer(indexer, type, isSetter: false);

        Assert.StartsWith("public int this[int index]", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Name = \"get_Item\"", string.Join("\n", accessors.Declarations.SelectMany(lines => lines)), StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ForwardedIntrinsicIndexerGetsTheSameAttribute()
    {
        var source = HandBuiltModel.Class("Source");
        source.Modifiers = new MemberModifiers { IsIntrinsic = true };
        var clone = HandBuiltModel.Class("Clone");
        clone.CloneSource = source;
        AddMember(clone, "field", "Item");
        var indexer = AddIndexer(clone, "Int32", intrinsic: true);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();

        IndexerEmitter.Emit(indexer, false, false, writer, diagnostics);

        Assert.StartsWith("[System.Runtime.CompilerServices.IndexerName(\"Indexer\")]", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("Value[index]", writer.ToString(), StringComparison.Ordinal);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ClrShellRetainsEventAndUnprojectableDeclarationNames()
    {
        var shell = Assert.IsAssignableFrom<MemberType>(TypeProjector.CreateShell(typeof(ClrNames), "Probe"));

        Assert.Contains("Item", shell.UnprojectedMemberNames);
        Assert.Contains("Indexer", shell.UnprojectedMemberNames);
        Assert.DoesNotContain("Slot", shell.UnprojectedMemberNames);
    }

    private sealed class ClrNames
    {
        public static event Action Item { add { } remove { } }

        private static int Indexer(int index)
        {
            return index;
        }

        [System.Runtime.CompilerServices.IndexerName("Slot")]
        public int this[int index] => Indexer(index);
    }

    private void AddMember(ClassType type, string kind, string name)
    {
        var integer = model.Type("Int32");
        switch(kind)
        {
            case "field":
                type.FieldsList.Add(new Field(name, integer, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = type });
                break;
            case "property":
                type.PropertiesList.Add(new Property(name, integer, DeclarationFragments.Public) { DeclaringType = type });
                break;
            case "method":
                type.MethodsList.Add(new Method(name, integer, [], DeclarationFragments.Public) { DeclaringType = type });
                break;
            case "nested":
                type.NestedTypes.Add(HandBuiltModel.Class(name));
                break;
        }
    }

    private Indexer AddIndexer(ClassType type, string parameterType, bool intrinsic = false)
    {
        var indexer = new Indexer(model.Type("Int32"), [new Parameter("index", model.Type(parameterType))],
            new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = intrinsic })
        {
            DeclaringType = type,
            Getter = new PropertyAccessor { IsAuto = true },
            Setter = new PropertyAccessor { IsAuto = true },
        };
        type.IndexersList.Add(indexer);
        return indexer;
    }
}