using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class QualifiedNominalBaseResolutionTests
{
    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 1)]
    [InlineData("struct", 0)]
    [InlineData("struct", 1)]
    public void QualifiedConcreteBaseRetainsItsDefinitionAndArguments(string kind, int arity)
    {
        var f = new Fragments();
        var definition = f.Nominal(kind, "Base", arity);
        var argument = f.Nominal("class", "Value");
        var entry = Base("Library.Base", arity == 0 ? null : Arguments("Library.Value"));
        var derived = Assert.IsAssignableFrom<ConcreteType>(f.Owner(kind, "Derived", entry));

        f.Resolve(derived);

        if(arity == 0)
        {
            Assert.Same(definition, derived.BaseType);
        }
        else
        {
            var constructed = Assert.IsType<ConstructedType>(derived.BaseType);
            Assert.Same(definition, constructed.Definition);
            Assert.Same(argument, Assert.Single(constructed.TypeArguments));
            Assert.Same(f.Model.TypeInterner.GetOrCreateConstructedType(definition, [argument]), constructed);
        }

        Assert.False(derived.BaseUnresolved);
        Assert.Empty(derived.BaseAbstractTypes);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 1)]
    [InlineData("struct", 1)]
    [InlineData("abstract", 1)]
    public void QualifiedAbstractBaseIsStoredOnItsImplementerOrRefinement(string kind, int arity)
    {
        var f = new Fragments();
        var definition = f.Nominal("abstract", "Contract", arity);
        var argument = f.Nominal("class", "Value");
        var owner = f.Owner(kind, "Owner", Base("Library.Contract", arity == 0 ? null : Arguments("Library.Value")));

        f.Resolve(owner);

        var resolved = Assert.Single(owner.BaseAbstractTypes);
        if(arity == 0)
        {
            Assert.Same(definition, resolved);
        }
        else
        {
            var constructed = Assert.IsType<ConstructedType>(resolved);
            Assert.Same(definition, constructed.Definition);
            Assert.Same(argument, Assert.Single(constructed.TypeArguments));
        }

        if(owner is ConcreteType concrete)
        {
            Assert.Null(concrete.BaseType);
            Assert.False(concrete.BaseUnresolved);
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("local")]
    [InlineData("imported")]
    [InlineData("imported-alias")]
    [InlineData("qualified-alias")]
    public void ExistingSimpleAndAliasSpellingsResolveToTheNominalBase(string spelling)
    {
        var f = new Fragments();
        var definition = f.Nominal("class", "Base", 1);
        var argument = f.Nominal("class", "Value");
        var expected = f.Model.TypeInterner.GetOrCreateConstructedType(definition, [argument]);
        var name = "Base";
        var typeArgs = Arguments("Library.Value");
        if(spelling is "imported-alias" or "qualified-alias")
        {
            f.Alias("BaseAlias", expected);
            name = spelling == "qualified-alias" ? "Library.BaseAlias" : "BaseAlias";
            typeArgs = null;
        }

        if(spelling == "local")
        {
            f.File.DeclareTopLevelType(definition.Name, definition);
        }
        else
        {
            f.File.DeclareImport("Library");
        }

        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived", Base(name, typeArgs)));

        f.Resolve(derived);

        Assert.Same(expected, derived.BaseType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("Library.Base", 1, 0, "Library.Base")]
    [InlineData("Library.Base", 1, 2, "Library.Base<,>")]
    [InlineData("Library.Base", 0, 1, "Library.Base<>")]
    [InlineData("Library.Missing", 0, 0, "Library.Missing")]
    [InlineData("Library.Missing", 0, 1, "Library.Missing<>")]
    public void MissingNamesAndArityMismatchesAreAnchoredAtTheWrittenBase(
        string name, int declaredArity, int writtenArity, string lookedUpName)
    {
        var f = new Fragments();
        _ = f.Nominal("class", "Base", declaredArity);
        _ = f.Nominal("class", "Value");
        var arguments = writtenArity == 0 ? null : Arguments([.. Enumerable.Repeat("Library.Value", writtenArity)]);
        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived", Base(name, arguments)));

        f.Resolve(derived);

        Assert.Null(derived.BaseType);
        Assert.True(derived.BaseUnresolved);
        Assert.Empty(derived.BaseAbstractTypes);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Number.Value);
        Assert.Equal($"The type '{lookedUpName}' could not be found.", diagnostic.Message);
        AssertBaseSite(diagnostic);
    }

    [Theory]
    [InlineData("class", "struct")]
    [InlineData("struct", "class")]
    public void QualifiedConcreteBaseOfTheWrongKindKeepsItsRefusal(string ownerKind, string baseKind)
    {
        var f = new Fragments();
        _ = f.Nominal(baseKind, "Base", 1);
        _ = f.Nominal("class", "Value");
        var derived = Assert.IsAssignableFrom<ConcreteType>(
            f.Owner(ownerKind, "Derived", Base("Library.Base", Arguments("Library.Value"))));

        f.Resolve(derived);

        Assert.Null(derived.BaseType);
        Assert.Empty(derived.BaseAbstractTypes);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_BASE_TYPE_KIND_MISMATCH", diagnostic.Number.Value);
        Assert.Equal($"A {ownerKind} cannot inherit from a {baseKind} ('Library.Base').", diagnostic.Message);
        AssertBaseSite(diagnostic);
    }

    [Fact]
    public void AnAdditionalQualifiedConcreteBaseKeepsItsRefusal()
    {
        var f = new Fragments();
        var first = f.Nominal("class", "First");
        _ = f.Nominal("class", "Second", 1);
        _ = f.Nominal("class", "Value");
        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived",
            Base("Library.First"), Base("Library.Second", Arguments("Library.Value"))));

        f.Resolve(derived);

        Assert.Same(first, derived.BaseType);
        Assert.Empty(derived.BaseAbstractTypes);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_CONCRETE_IN_ABSTRACT_LIST", diagnostic.Number.Value);
        Assert.Contains("'Library.Second'", diagnostic.Message, StringComparison.Ordinal);
        AssertBaseSite(diagnostic);
    }

    [Fact]
    public void AnUnknownQualifiedAbstractDoesNotDiscardTheResolvedConcreteBase()
    {
        var f = new Fragments();
        var first = f.Nominal("class", "First");
        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived", Base("Library.First"), Base("Library.Missing")));

        f.Resolve(derived);

        Assert.Same(first, derived.BaseType);
        Assert.False(derived.BaseUnresolved);
        Assert.Empty(derived.BaseAbstractTypes);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Number.Value);
        Assert.Equal("The type 'Library.Missing' could not be found.", diagnostic.Message);
        AssertBaseSite(diagnostic);
    }

    [Fact]
    public void AQualifiedNonNominalAliasDoesNotBecomeAPermittedBase()
    {
        var f = new Fragments();
        var left = f.Nominal("class", "Left");
        var right = f.Nominal("class", "Right");
        f.Alias("Choice", f.Model.TypeInterner.GetOrCreateUnionType([left, right]));
        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived", Base("Library.Choice")));

        f.Resolve(derived);

        Assert.Null(derived.BaseType);
        Assert.Empty(derived.BaseAbstractTypes);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_INVALID_BASE_TYPE", diagnostic.Number.Value);
        Assert.Equal("'Library.Choice' is not an abstract and cannot be used as a base type.", diagnostic.Message);
        AssertBaseSite(diagnostic);
    }

    [Theory]
    [InlineData("class", 0)]
    [InlineData("class", 1)]
    [InlineData("struct", 0)]
    [InlineData("struct", 1)]
    public void AConstructedEnclosingBaseRetainsItsNestedIdentity(string kind, int ownArity)
    {
        var f = new Fragments();
        var outer = f.Nominal("class", "Outer", 1);
        var nested = f.Nominal(kind, "Base", ownArity, outer);
        var firstArgument = f.Nominal("class", "First");
        var secondArgument = f.Nominal("class", "Second");
        var ownArgument = f.Nominal("class", "Value");
        var first = Assert.IsAssignableFrom<ConcreteType>(f.Owner(kind, "FirstDerived", NestedBase("First", ownArity)));
        var second = Assert.IsAssignableFrom<ConcreteType>(f.Owner(kind, "SecondDerived", NestedBase("Second", ownArity)));
        var repeated = Assert.IsAssignableFrom<ConcreteType>(f.Owner(kind, "RepeatedDerived", NestedBase("First", ownArity)));

        f.Resolve(first);
        f.Resolve(second);
        f.Resolve(repeated);

        var firstBase = Assert.IsType<ConstructedType>(first.BaseType);
        var secondBase = Assert.IsType<ConstructedType>(second.BaseType);
        var firstOuter = f.Model.TypeInterner.GetOrCreateConstructedType(outer, [firstArgument]);
        var secondOuter = f.Model.TypeInterner.GetOrCreateConstructedType(outer, [secondArgument]);
        IReadOnlyList<IMistType> ownArguments = ownArity == 0 ? [] : [ownArgument];
        Assert.Same(nested, firstBase.Definition);
        Assert.Same(firstOuter, firstBase.Enclosing);
        Assert.Equal(ownArguments, firstBase.TypeArguments);
        Assert.Same(f.Model.TypeInterner.GetOrCreateConstructedType(nested, ownArguments, firstOuter), firstBase);
        Assert.Same(secondOuter, secondBase.Enclosing);
        Assert.NotSame(firstBase, secondBase);
        Assert.Same(firstBase, repeated.BaseType);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AConstructedEnclosingAbstractRetainsItsEnclosingAndOwnArguments()
    {
        var f = new Fragments();
        var outer = f.Nominal("class", "Outer", 1);
        var contract = f.Nominal("abstract", "Contract", 1, outer);
        var outerArgument = f.Nominal("class", "First");
        var ownArgument = f.Nominal("class", "Value");
        var entry = NestedBase("First", 1);
        entry.Enclosing!.Nested = Name("Contract");
        var owner = f.Owner("abstract", "Refinement", entry);

        f.Resolve(owner);

        var resolved = Assert.IsType<ConstructedType>(Assert.Single(owner.BaseAbstractTypes));
        Assert.Same(contract, resolved.Definition);
        Assert.Same(ownArgument, Assert.Single(resolved.TypeArguments));
        Assert.Same(f.Model.TypeInterner.GetOrCreateConstructedType(outer, [outerArgument]), resolved.Enclosing);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void QualifiedBaseNamingRetainsTheSiteAndOwnerForAccessibilityJudgment()
    {
        var f = new Fragments();
        var outer = f.Nominal("class", "Outer");
        var nested = f.Nominal("class", "Base", declaring: outer);
        var entry = Base("Library.Outer.Base");
        var derived = Assert.IsType<ClassType>(f.Owner("class", "Derived", entry));

        f.Resolve(derived);

        Assert.Same(nested, derived.BaseType);
        var naming = Assert.Single(f.Model.TypeNamings.Namings);
        Assert.Same(nested, naming.Named);
        Assert.Same(entry, naming.Node);
        Assert.Same(derived, naming.Owner);
        Assert.Same(f.Model.SelfAssembly, naming.Assembly);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static BaseTypeRefNode NestedBase(string outerArgument, int ownArity)
    {
        var entry = Base("Library.Outer", ownArity == 0 ? null : Arguments("Library.Value"));
        entry.Enclosing = new BaseTypeEnclosingNode { OuterArgs = Arguments($"Library.{outerArgument}"), Nested = Name("Base") };
        return entry;
    }

    private static BaseTypeRefNode Base(string name, TypeArgumentListNode? typeArgs = null)
    {
        var node = BindingFragments.At<BaseTypeRefNode>(50);
        node.Name = Name(name);
        node.TypeArgs = typeArgs;
        return node;
    }

    private static QualifiedIdentifierNode Name(string name)
    {
        var segments = name.Split('.');
        return new QualifiedIdentifierNode
        {
            IDENTIFIER = BindingFragments.Token(segments[0]),
            IDENTIFIERs = [.. segments.Skip(1).Select(BindingFragments.Token)],
        };
    }

    private static TypeArgumentListNode Arguments(params string[] names)
    {
        return new TypeArgumentListNode
        {
            TypeArgument = Argument(names[0]),
            TypeArguments = [.. names.Skip(1).Select(Argument)],
        };
    }

    private static TypeArgumentNode Argument(string name)
    {
        return new TypeReferenceTypeArgumentTypeArgumentNode
        {
            Type = new TypeReferenceNode { First = new NamedSingleTypeRefSingleTypeRefNode { Name = Name(name) } },
        };
    }

    private static void AssertBaseSite(Diagnostic diagnostic)
    {
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(1, diagnostic.Position().Start.Line);
        Assert.Equal(51, diagnostic.Position().Start.Column);
        Assert.Equal(52, diagnostic.Position().End.Column);
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
        }

        public MemberType Nominal(string kind, string name, int arity = 0, MistType? declaring = null)
        {
            var definitionName = arity == 0 ? name : ScopeResolver.BuildArityName(name, arity);
            var type = Create(kind, definitionName, "Library", declaring?.Scope ?? File, declaring, null);
            for(var i = 0; i < arity; i++)
            {
                type.TypeParametersList.Add(new TypeParameter($"T{i}", i, type.Scope, type));
            }

            declaring?.NestedTypes.Add(type);
            _ = Model.GetOrAddNamespace("Library");
            Assert.True(Model.RegisterType(type));
            return type;
        }

        public void Alias(string name, IMistType target)
        {
            var alias = new TypeAlias(name, "Library", File, null, new TypeAliasDeclarationNode()) { AliasedType = target };
            Assert.True(Model.RegisterType(alias));
        }

        public MemberType Owner(string kind, string name, params BaseTypeRefNode[] entries)
        {
            var declaration = new BodyTypeTypeDeclarationNode
            {
                TypeBody = new TypeBodyDeclarationNode
                {
                    BaseList = new BaseListNode { First = entries[0], BaseTypeRefs = [.. entries.Skip(1)] },
                },
            };
            return Create(kind, name, "Application", File, null, declaration);
        }

        public void Resolve(MemberType owner)
        {
            new BaseTypeResolver(Context, Context.Resolver).Resolve(owner.DeclarationNode!, owner);
        }

        private static MemberType Create(
            string kind, string name, string namespaceName, Scope parent, MistType? declaring, TypeDeclarationNode? declaration)
        {
            return kind switch
            {
                "class" => new ClassType(name, namespaceName, parent, declaring, declaration),
                "struct" => new StructType(name, namespaceName, parent, declaring, declaration),
                "abstract" => new AbstractType(name, namespaceName, parent, declaring, declaration),
                _ => throw new ArgumentException("Unknown type kind", nameof(kind)),
            };
        }
    }
}