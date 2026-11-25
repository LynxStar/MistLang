using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

/// <summary>
/// The base list's positional rule: only a concrete owner's first entry may be its concrete base,
/// and every other entry is classified on its own into the abstract list (MIST0306-MIST0308).
/// </summary>
[Trait("Tier", "2")]
public sealed class BaseListClassificationTests
{
    [Theory]
    [InlineData("same-kind")]
    [InlineData("other-kind")]
    [InlineData("missing")]
    [InlineData("abstract")]
    public void EveryOutcomeOfTheFirstEntryLeavesTheLaterAbstractToTheAbstractList(string first)
    {
        var f = new Fragments();
        var written = first switch
        {
            "same-kind" => f.Declare("class", "Base"),
            "other-kind" => f.Declare("struct", "Base"),
            "abstract" => f.Declare("abstract", "Base"),
            _ => null,
        };
        var later = f.Declare("abstract", "Later");
        var owner = Assert.IsType<ClassType>(f.Owner("class", Entry("Base", 10), Entry("Later", 20)));

        f.Resolve(owner);

        Assert.Same(first == "same-kind" ? written : null, owner.BaseType);
        MemberType[] abstracts = first == "abstract" ? [written!, later] : [later];
        Assert.Equal(abstracts.Length, owner.BaseAbstractTypes.Count);
        Assert.All(abstracts.Zip(owner.BaseAbstractTypes), pair => Assert.Same(pair.First, pair.Second));
        var diagnostics = f.Diagnostics.ToImmutableArray();
        switch(first)
        {
            case "other-kind":
                AssertReported(Assert.Single(diagnostics), "MIST0306", "A class cannot inherit from a struct ('Base').", 10);
                break;
            case "missing":
                Assert.True(owner.BaseUnresolved);
                AssertReported(Assert.Single(diagnostics), "MIST0298", "The type 'Base' could not be found.", 10);
                break;
            default:
                Assert.False(owner.BaseUnresolved);
                Assert.Empty(diagnostics);
                break;
        }
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public void ASameKindConcreteAfterAnAbstractFirstEntryIsNotTheBase(string kind)
    {
        var f = new Fragments();
        var contract = f.Declare("abstract", "Contract");
        _ = f.Declare(kind, "Base");
        var owner = Assert.IsAssignableFrom<ConcreteType>(f.Owner(kind, Entry("Contract", 10), Entry("Base", 20)));

        f.Resolve(owner);

        Assert.Null(owner.BaseType);
        Assert.False(owner.BaseUnresolved);
        Assert.Same(contract, Assert.Single(owner.BaseAbstractTypes));
        AssertReported(Assert.Single(f.Diagnostics.ToImmutableArray()), "MIST0307",
            "A concrete type ('Base') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.", 20);
    }

    [Theory]
    [InlineData("class", 0)]
    [InlineData("struct", 1)]
    public void AnAbstractOwnerRefusesAConcreteEvenAsItsFirstEntry(string kind, int arity)
    {
        var f = new Fragments();
        _ = f.Declare(kind, "Base", arity);
        _ = f.Declare("class", "Value");
        var later = f.Declare("abstract", "Later");
        var owner = Assert.IsType<AbstractType>(
            f.Owner("abstract", Entry("Base", 10, arity == 0 ? null : BindingFragments.TypeArguments("Value")), Entry("Later", 20)));

        f.Resolve(owner);

        Assert.Same(later, Assert.Single(owner.BaseAbstractTypes));
        AssertReported(Assert.Single(f.Diagnostics.ToImmutableArray()), "MIST0307",
            "A concrete type ('Base') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.", 10);
    }

    [Fact]
    public void EachLaterEntryIsClassifiedOnItsOwnAndTheAbstractsKeepTheirWrittenOrder()
    {
        var f = new Fragments();
        var concrete = f.Declare("class", "Base");
        var first = f.Declare("abstract", "First");
        _ = f.Declare("class", "Other");
        var generic = f.Declare("abstract", "Generic", 1);
        var value = f.Declare("class", "Value");
        f.Declare(new EnumType("Kind", "Application", f.File, null, null));
        var last = f.Declare("abstract", "Last");
        var owner = Assert.IsType<ClassType>(f.Owner("class",
            Entry("Base", 10),
            Entry("First", 20),
            Entry("Missing", 30),
            Entry("Other", 40),
            Entry("Generic", 50, BindingFragments.TypeArguments("Value")),
            Entry("Kind", 60),
            Entry("Last", 70)));

        f.Resolve(owner);

        Assert.Same(concrete, owner.BaseType);
        Assert.False(owner.BaseUnresolved);
        Assert.Collection(owner.BaseAbstractTypes,
            resolved => Assert.Same(first, resolved),
            resolved => Assert.Same(f.Model.TypeInterner.GetOrCreateConstructedType(generic, [value]), resolved),
            resolved => Assert.Same(last, resolved));
        Assert.Collection(f.Diagnostics.ToImmutableArray(),
            diagnostic => AssertReported(diagnostic, "MIST0298", "The type 'Missing' could not be found.", 30),
            diagnostic => AssertReported(diagnostic, "MIST0307",
                "A concrete type ('Other') cannot appear in the abstract implementations list. Only the first entry may be a concrete base type.", 40),
            diagnostic => AssertReported(diagnostic, "MIST0308", "'Kind' is not an abstract and cannot be used as a base type.", 60));
    }

    private static BaseTypeRefNode Entry(string name, int offset, TypeArgumentListNode? typeArgs = null)
    {
        var node = BindingFragments.At<BaseTypeRefNode>(offset);
        node.Name = BindingFragments.Name(name);
        node.TypeArgs = typeArgs;
        return node;
    }

    private static void AssertReported(Diagnostic diagnostic, string number, string message, int offset)
    {
        Assert.Equal(number, diagnostic.Number.Value);
        Assert.Equal(message, diagnostic.Message);
        Assert.Equal(offset + 1, diagnostic.Position().Start.Column);
    }

    private sealed class Fragments
    {
        public Model Model { get; } = new();

        public DiagnosticBag Diagnostics { get; } = new();

        public BuilderContext Context { get; }

        public FileScope File { get; }

        public Fragments()
        {
            var assembly = Model.AddSelfAssembly("Bases");
            Context = new BuilderContext(Model, Diagnostics, NullAmbientTypeProvider.Instance);
            File = new FileScope(Context.ScopeFor(assembly), Model, Diagnostics);
            _ = Model.GetOrAddNamespace("Application");
        }

        public MemberType Declare(string kind, string name, int arity = 0)
        {
            var type = Create(kind, arity == 0 ? name : ScopeResolver.BuildArityName(name, arity), null);
            for(var i = 0; i < arity; i++)
            {
                type.TypeParametersList.Add(new TypeParameter($"T{i}", i, type.Scope, type));
            }

            Declare(type);
            return type;
        }

        public void Declare(DeclaredType type)
        {
            Assert.True(Model.RegisterType(type));
            File.DeclareTopLevelType(type.Name, type);
        }

        public MemberType Owner(string kind, params BaseTypeRefNode[] entries)
        {
            var declaration = new BodyTypeTypeDeclarationNode
            {
                TypeBody = new TypeBodyDeclarationNode
                {
                    BaseList = new BaseListNode { First = entries[0], BaseTypeRefs = [.. entries.Skip(1)] },
                },
            };
            return Create(kind, "Owner", declaration);
        }

        public void Resolve(MemberType owner)
        {
            new BaseTypeResolver(Context, Context.Resolver).Resolve(owner.DeclarationNode!, owner);
        }

        private MemberType Create(string kind, string name, TypeDeclarationNode? declaration)
        {
            return kind switch
            {
                "class" => new ClassType(name, "Application", File, null, declaration),
                "struct" => new StructType(name, "Application", File, null, declaration),
                "abstract" => new AbstractType(name, "Application", File, null, declaration),
                _ => throw new ArgumentException("Unknown type kind", nameof(kind)),
            };
        }
    }
}