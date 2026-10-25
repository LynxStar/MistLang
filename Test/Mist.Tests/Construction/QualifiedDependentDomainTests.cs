using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

[Trait("Tier", "2")]
public sealed class QualifiedDependentDomainTests
{
    [Theory]
    [InlineData("Measures.Left.Small", null, null)]
    [InlineData("Left.Small", "Measures", null)]
    [InlineData("Left.Small", null, "Measures")]
    [InlineData("Small", null, "Measures.Left")]
    public void NamedDomainsKeepTheirFullSpellingAndResolveThroughNamespacesAndImports(
        string spelling, string? enclosingNamespace, string? import)
    {
        var f = new Domains();
        var expected = f.Alias("Measures.Left", "Small", HandBuiltModel.Refined(f.Primitive("Int32")));
        _ = f.Alias("Measures.Right", "Small", f.Primitive("Boolean"));

        if(import is not null)
        {
            f.File.DeclareImport(import);
        }

        var scope = enclosingNamespace is null
            ? (Scope)f.File
            : new NamespaceScope(f.File, enclosingNamespace, f.Model);
        var annotation = Named("N", spelling);
        var owner = f.Populate(scope, List(annotation));
        var parameter = Assert.Single(owner.TypeParametersList);

        Assert.True(parameter.IsDependent);
        Assert.Equal(spelling, parameter.DomainTypeName);
        Assert.Same(expected, parameter.Domain);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var naming = Assert.Single(f.Model.TypeNamings.Namings);
        Assert.Same(expected, naming.Named);
        Assert.Same(annotation.ConstraintName, naming.Node);
    }

    [Theory]
    [InlineData("Measures.Outer.Small")]
    [InlineData("Outer.Small")]
    public void AQualifiedNestedAliasResolvesInTheDeclaringNamespace(string spelling)
    {
        var f = new Domains();
        var scope = new NamespaceScope(f.File, "Measures", f.Model);
        var outer = f.Class("Measures", "Outer", scope);
        var expected = f.Alias("Measures", "Small", f.Primitive("Int32"), outer);
        var owner = f.Populate(scope, List(Named("N", spelling)));

        Assert.Same(expected, Assert.Single(owner.TypeParametersList).Domain);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("Int32", true)]
    [InlineData("UInt32", true)]
    [InlineData("Int64", true)]
    [InlineData("UInt64", true)]
    [InlineData("Int16", true)]
    [InlineData("UInt16", true)]
    [InlineData("Byte", true)]
    [InlineData("SByte", true)]
    [InlineData("Boolean", true)]
    [InlineData("Char", true)]
    [InlineData("Single", false)]
    [InlineData("Double", false)]
    [InlineData("Decimal", false)]
    [InlineData("String", false)]
    [InlineData("Regex", false)]
    public void QualifiedAliasAndRefinementChainsUseTheSameCarryableDomains(string primitive, bool admitted)
    {
        var f = new Domains();
        var refined = f.Alias("Measures", "Small", HandBuiltModel.Refined(f.Primitive(primitive)));
        var expected = f.Alias("Measures", "Range", refined);
        var annotation = Named("N", "Measures.Range");
        var owner = f.Populate(f.File, List(annotation));

        Assert.Same(expected, Assert.Single(owner.TypeParametersList).Domain);

        if(admitted)
        {
            Assert.Empty(f.Diagnostics.ToImmutableArray());
        }
        else
        {
            var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
            Assert.Equal("MIST_DT_0001", diagnostic.Number.Value);
            Assert.Equal("hand-built.mist", diagnostic.Location.Path);
            Assert.Equal(31, diagnostic.Position().Start.Column);
        }
    }

    [Theory]
    [InlineData("Missing.Small")]
    [InlineData("Measures")]
    public void AnUnknownOrNamespaceOnlyDomainReportsItsFullNameWithoutBecomingAnOrdinaryParameter(string spelling)
    {
        var f = new Domains();
        _ = f.Model.GetOrAddNamespace("Measures");
        var owner = f.Populate(f.File, List(Named("N", spelling)));
        var parameter = Assert.Single(owner.TypeParametersList);
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());

        Assert.True(parameter.IsDependent);
        Assert.Null(parameter.Domain);
        Assert.Equal(spelling, parameter.DomainTypeName);
        Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Number.Value);
        Assert.Contains(spelling, diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(31, diagnostic.Position().Start.Column);
        Assert.Empty(f.Model.TypeNamings.Namings);
    }

    [Fact]
    public void AmbiguousImportedQualifiedDomainsReportTheAnnotationAndRemainUnresolved()
    {
        var f = new Domains();
        _ = f.Alias("Left.Ranges", "Small", f.Primitive("Int32"));
        _ = f.Alias("Right.Ranges", "Small", f.Primitive("Int32"));
        f.File.DeclareImport("Left");
        f.File.DeclareImport("Right");
        var owner = f.Populate(f.File, List(Named("N", "Ranges.Small")));
        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());

        Assert.Null(Assert.Single(owner.TypeParametersList).Domain);
        Assert.Equal("MIST_PM_AMBIGUOUS_TYPE", diagnostic.Number.Value);
        Assert.Contains("Ranges.Small", diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(31, diagnostic.Position().Start.Column);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("alias")]
    [InlineData("newtype")]
    public void DeferredResolutionAnchorsEachDomainWithinItsDeclaration(string declarationKind)
    {
        var f = new Domains();
        var first = Named("N", "Missing.First", offset: 20);
        var second = Named("M", "Missing.Second", offset: 40);
        var parameters = List(first, second);
        var owner = f.Populate(f.File, parameters, resolve: false);
        SyntaxNode declaration = declarationKind switch
        {
            "alias" => new TypeAliasDeclarationNode { TypeParams = parameters },
            "newtype" => new NewtypeDeclarationNode { TypeParams = parameters },
            _ => new BodyTypeTypeDeclarationNode { TypeBody = new TypeBodyDeclarationNode { TypeParams = parameters } },
        };

        f.Context.Domains.Resolve(owner.TypeParametersList, declaration);

        var diagnostics = f.Diagnostics.ToImmutableArray();
        Assert.Equal([21, 41], diagnostics.Select(diagnostic => diagnostic.Position().Start.Column));
        Assert.All(diagnostics, diagnostic => Assert.Equal("MIST_PM_UNRESOLVED_TYPE", diagnostic.Number.Value));
        Assert.All(owner.TypeParametersList, parameter => Assert.Null(parameter.Domain));
    }

    [Fact]
    public void APrivateQualifiedDomainRetainsTheAliasAndUseSiteForAccessibilityAnalysis()
    {
        var f = new Domains();
        var outer = f.Class("Measures", "Outer", f.File);
        var expected = f.Alias("Measures", "Small", f.Primitive("Int32"), outer, Accessibility.Private);
        var annotation = Named("N", "Measures.Outer.Small");
        var owner = f.Populate(f.File, List(annotation));
        var naming = Assert.Single(f.Model.TypeNamings.Namings);

        Assert.Same(expected, Assert.Single(owner.TypeParametersList).Domain);
        Assert.Same(expected, naming.Named);
        Assert.Same(owner, naming.Owner);
        Assert.Same(f.Model.SelfAssembly, naming.Assembly);
        Assert.Same(annotation.ConstraintName, naming.Node);
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    private static NamedConstraintDependentTypeParameterTypeParameterNode Named(string parameterName, string domainName, int offset = 30)
    {
        var parts = domainName.Split('.');
        var name = BindingFragments.At<QualifiedIdentifierNode>(offset);
        name.IDENTIFIER = BindingFragments.Token(parts[0]);
        name.IDENTIFIERs = [.. parts.Skip(1).Select(BindingFragments.Token)];
        return new NamedConstraintDependentTypeParameterTypeParameterNode
        {
            Name = BindingFragments.Token(parameterName),
            ConstraintName = name,
        };
    }

    private static TypeParameterListNode List(TypeParameterNode first, params TypeParameterNode[] rest)
    {
        return new TypeParameterListNode { TypeParameter = first, TypeParameters = [.. rest] };
    }

    private sealed class Domains
    {
        private readonly HandBuiltModel built = new();

        public Model Model => built.Model;
        public DiagnosticBag Diagnostics { get; } = new();
        public BuilderContext Context { get; }
        public FileScope File { get; }

        public Domains()
        {
            _ = Model.AddSelfAssembly("Test");
            Context = new BuilderContext(Model, Diagnostics, NullAmbientTypeProvider.Instance);
            File = new FileScope(Context.ScopeFor(Model.SelfAssembly!), Model, Diagnostics);
        }

        public ClassType Primitive(string name)
        {
            var type = built.Type(name);
            _ = Model.RegisterType(type);
            return type;
        }

        public ClassType Class(string namespaceName, string name, Scope scope)
        {
            _ = Model.GetOrAddNamespace(namespaceName);
            var type = new ClassType(name, namespaceName, scope, null, null)
            {
                Assembly = Model.SelfAssembly!,
                Modifiers = new MemberModifiers { Accessibility = Accessibility.Public },
            };
            _ = Model.RegisterType(type);
            return type;
        }

        public TypeAlias Alias(string namespaceName, string name, IMistType aliased, MistType? outer = null, Accessibility accessibility = Accessibility.Public)
        {
            _ = Model.GetOrAddNamespace(namespaceName);
            var type = new TypeAlias(name, namespaceName, outer?.Scope ?? File, outer, new TypeAliasDeclarationNode())
            {
                AliasedType = aliased,
                Assembly = Model.SelfAssembly!,
                Modifiers = new MemberModifiers { Accessibility = accessibility },
            };
            outer?.NestedTypes.Add(type);
            _ = Model.RegisterType(type);
            return type;
        }

        public ClassType Populate(Scope scope, TypeParameterListNode parameters, bool resolve = true)
        {
            var type = Class("Probe", "Box<>", scope);
            TypeParameterPopulator.Populate(type, type.Scope, type, parameters, Diagnostics, resolve ? Context.Domains : null);
            return type;
        }
    }
}