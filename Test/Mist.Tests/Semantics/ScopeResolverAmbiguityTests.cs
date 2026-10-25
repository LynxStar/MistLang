using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

/// <summary>
/// Unit tests for namespace-level name resolution: which level answers, and when two answers at
/// the SAME level are an ambiguity rather than a contest.
/// <para>
/// The levels, innermost first, are: an enclosing namespace, the file (its own top-level
/// declarations, then its usings), the project's global usings, and finally the global namespace.
/// An inner level that answers is never followed by an outer one, so shadowing -- not accumulation
/// -- is what happens between them.
/// </para>
/// </summary>
public sealed class ScopeResolverAmbiguityTests
{
    /// <summary>
    /// Creates a minimal BuilderContext with the given ProgramModel.
    /// </summary>
    private static (BuilderContext Context, DiagnosticBag Diagnostics) CreateContext()
    {
        var programModel = new ProgramModelClass();
        var diagnostics = new DiagnosticBag();
        _ = programModel.AddSelfAssembly("Test");
        var context = new BuilderContext(programModel, diagnostics, NullAmbientTypeProvider.Instance);
        return (context, diagnostics);
    }

    /// <summary>
    /// The scope holding the implicit usings every file in these tests resolves through. Implicit
    /// usings belong to an assembly, so a single-assembly compilation is what makes one level of them
    /// the only level.
    /// </summary>
    private static AssemblyScope AssemblyScopeOf(BuilderContext context)
    {
        return context.ScopeFor(context.ProgramModel.SelfAssembly!);
    }

    /// <summary>
    /// A file scope hanging off its assembly's scope.
    /// </summary>
    private static FileScope NewFileScope(BuilderContext context, DiagnosticBag diagnostics)
    {
        return new FileScope(AssemblyScopeOf(context), context.ProgramModel, diagnostics);
    }

    /// <summary>
    /// Registers a type in the program model under the given namespace.
    /// </summary>
    private static MistType RegisterType(ProgramModelClass programModel, string namespaceName, string typeName)
    {
        _ = programModel.GetOrAddNamespace(namespaceName);
        var type = new ClassType(typeName, namespaceName, new Scope(null), null, null);
        _ = programModel.RegisterType(type);
        return type;
    }

    private static MistType RegisterSourceType(
        ProgramModelClass programModel, string namespaceName, string typeName)
    {
        return RegisterWithAssembly(programModel, namespaceName, typeName, programModel.SelfAssembly!);
    }

    private static MistType RegisterProjectedType(
        ProgramModelClass programModel, string namespaceName, string typeName)
    {
        return RegisterWithAssembly(programModel, namespaceName, typeName, MistAssembly.Ambient);
    }

    private static MistType RegisterWithAssembly(
        ProgramModelClass programModel, string namespaceName, string typeName, MistAssembly assembly)
    {
        var type = RegisterType(programModel, namespaceName, typeName);

        if(type is DeclaredType declared)
        {
            declared.Assembly = assembly;
        }

        return type;
    }

    /// <summary>
    /// Creates a dummy AST node that ResolveNamedType needs for diagnostic reporting.
    /// Uses a generated node type since SyntaxNode is abstract.
    /// </summary>
    private static Storm.Runtime.SyntaxNode MakeDummyNode()
    {
        return new CompilationUnitNode
        {
            Items = ImmutableArray<TopLevelItemNode>.Empty,
        };
    }

    private static void AssertAmbiguous(IMistType result, DiagnosticBag diagnostics, string because)
    {
        var diags = diagnostics.ToImmutableArray();
        Assert.True(
            result is ErrorType || diags.Any(d => d.Number.Value == "MIST_PM_AMBIGUOUS_TYPE"),
            $"{because} Got {result.GetType().Name} with {diags.Length} diagnostics.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Ambiguity WITHIN one level
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Two usings on the SAME level both providing the name is an ambiguity: neither outranks the
    /// other, so there is nothing to choose between them.
    /// </summary>
    [Fact]
    public void TwoImportedNamespaces_SameTypeName_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Foo");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");
        scope.DeclareImport("NsB");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        AssertAmbiguous(result, diagnostics, "Two usings on one level providing the same name is ambiguous.");
    }

    /// <summary>
    /// Ambiguity does not require that every using match -- only that two of them do.
    /// </summary>
    [Fact]
    public void ThreeImports_TwoHaveSameName_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Bar");
        _ = RegisterType(context.ProgramModel, "NsC", "Foo");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");
        scope.DeclareImport("NsB");
        scope.DeclareImport("NsC");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        AssertAmbiguous(result, diagnostics, "Two of three usings providing the same name is ambiguous.");
    }

    /// <summary>
    /// Arity-bearing names (<c>List&lt;&gt;</c>) go through the same lookup as simple ones, so they
    /// are ambiguous under the same conditions.
    /// </summary>
    [Fact]
    public void TwoImportedNamespaces_SameArityBearingName_ReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "List<>");
        _ = RegisterType(context.ProgramModel, "NsB", "List<>");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");
        scope.DeclareImport("NsB");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("List<>", scope, MakeDummyNode());

        AssertAmbiguous(result, diagnostics, "Generic names are ambiguous on the same terms as simple ones.");
    }

    /// <summary>
    /// The same namespace named twice on one level collapses, so it cannot conflict with itself.
    /// </summary>
    [Fact]
    public void SameNamespaceImportedTwice_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "NsA", "Foo");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");
        scope.DeclareImport("NsA");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        Assert.Same(foo, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// One using matching is just resolution.
    /// </summary>
    [Fact]
    public void SingleImportedNamespace_ResolvesCleanly()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "NsA", "Foo");
        _ = RegisterType(context.ProgramModel, "NsB", "Bar");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");
        scope.DeclareImport("NsB");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", scope, MakeDummyNode());

        Assert.Same(foo, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Source-declared imports outrank CLR-projected ones, WITHIN one level. The tiers and the
    // reason for them are ImportingScope's.
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SourceDeclaredImport_OutranksProjectedImport_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        var declared = RegisterSourceType(context.ProgramModel, "Standard", "Console");
        _ = RegisterProjectedType(context.ProgramModel, "System", "Console");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("Standard");
        scope.DeclareImport("System");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Console", scope, MakeDummyNode());

        Assert.Same(declared, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void ProjectedImport_AnswersWhenNoSourceDeclaredOneDoes()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterSourceType(context.ProgramModel, "Standard", "Console");
        var projected = RegisterProjectedType(context.ProgramModel, "System", "StringBuilder");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("Standard");
        scope.DeclareImport("System");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("StringBuilder", scope, MakeDummyNode());

        Assert.Same(projected, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void TwoProjectedImports_SameTypeName_StillReportsAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterProjectedType(context.ProgramModel, "System", "Timer");
        _ = RegisterProjectedType(context.ProgramModel, "System.Threading", "Timer");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("System");
        scope.DeclareImport("System.Threading");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Timer", scope, MakeDummyNode());

        AssertAmbiguous(result, diagnostics, "Two projected usings have no tier to separate them.");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Shadowing BETWEEN levels
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A file's using shadows a global using of another namespace holding the same name. The file
    /// level answers, so the global level is never asked -- this is C#'s rule, and the reason the
    /// two using levels are separate scopes rather than one pool.
    /// </summary>
    [Fact]
    public void FileUsing_ShadowsGlobalUsing_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "System", "Foo");
        var mine = RegisterType(context.ProgramModel, "MyLib", "Foo");

        AssemblyScopeOf(context).DeclareImport("System");

        var fileScope = NewFileScope(context, diagnostics);
        fileScope.DeclareImport("MyLib");

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", fileScope, MakeDummyNode());

        Assert.Same(mine, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// A global using still answers when the file has nothing to say about the name.
    /// </summary>
    [Fact]
    public void GlobalUsing_AnswersWhenFileDoesNot()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "System", "Foo");
        AssemblyScopeOf(context).DeclareImport("System");

        var fileScope = NewFileScope(context, diagnostics);

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", fileScope, MakeDummyNode());

        Assert.Same(foo, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// The enclosing namespace shadows the file's usings: a type in your own namespace wins over
    /// one you merely imported.
    /// </summary>
    [Fact]
    public void EnclosingNamespace_ShadowsFileUsing_NoAmbiguity()
    {
        var (context, diagnostics) = CreateContext();

        var ownFoo = RegisterType(context.ProgramModel, "MyNamespace", "Foo");
        _ = RegisterType(context.ProgramModel, "OtherNs", "Foo");

        var fileScope = NewFileScope(context, diagnostics);
        fileScope.DeclareImport("OtherNs");

        var namespaceScope = new NamespaceScope(fileScope, "MyNamespace", context.ProgramModel);

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", namespaceScope, MakeDummyNode());

        Assert.Same(ownFoo, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// Same-namespace scoping covers the WHOLE namespace, not just the file's part of it: a type
    /// declared in a sibling file is reachable by simple name with no using.
    /// </summary>
    [Fact]
    public void EnclosingNamespace_SeesTypesFromOtherFiles()
    {
        var (context, diagnostics) = CreateContext();

        var sibling = RegisterType(context.ProgramModel, "MyNamespace", "Sibling");

        var fileScope = NewFileScope(context, diagnostics);
        var namespaceScope = new NamespaceScope(fileScope, "MyNamespace", context.ProgramModel);

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Sibling", namespaceScope, MakeDummyNode());

        Assert.Same(sibling, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// A type parameter shadows every namespace level, at any depth.
    /// </summary>
    [Fact]
    public void LocalType_ShadowsGlobalAndFileImports()
    {
        var (context, diagnostics) = CreateContext();

        _ = RegisterType(context.ProgramModel, "NsA", "T");
        _ = RegisterType(context.ProgramModel, "NsB", "T");

        AssemblyScopeOf(context).DeclareImport("NsA");

        var fileScope = NewFileScope(context, diagnostics);
        fileScope.DeclareImport("NsB");

        var memberScope = new MemberScope(fileScope);
        var localT = new TypeParameter("T", 0, memberScope, null);
        memberScope.DeclareType("T", localT);

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("T", memberScope, MakeDummyNode());

        Assert.Same(localT, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // A file's top-level declarations, seen from its namespace blocks
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A type written at a file's top level is nameable from a namespace block in the SAME file.
    /// This is what makes <c>type Foo = int;</c> above a <c>namespace Bar { }</c> usable inside it,
    /// and it is narrower than a using: only the file's own declarations, not the namespace.
    /// </summary>
    [Fact]
    public void TopLevelType_IsVisibleFromNamespaceBlockInSameFile()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "App.Widget", "Foo");

        var fileScope = NewFileScope(context, diagnostics);
        fileScope.DeclareTopLevelType("Foo", foo);

        // A block namespace is a SIBLING of the file's namespace, so its parent is the file scope.
        var blockScope = new NamespaceScope(fileScope, "Bar", context.ProgramModel);

        var resolver = new ScopeResolver(context);
        var result = resolver.ResolveNamedType("Foo", blockScope, MakeDummyNode());

        Assert.Same(foo, result);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    /// <summary>
    /// The other half of the same rule: the block sees the file's own top-level declarations and
    /// NOT the rest of their namespace. A sibling file's contribution to <c>App.Widget</c> needs a
    /// using, because <c>Bar</c> is not nested inside <c>App.Widget</c>.
    /// </summary>
    [Fact]
    public void NamespaceBlock_DoesNotSeeRestOfFileNamespace()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "App.Widget", "Foo");
        _ = RegisterType(context.ProgramModel, "App.Widget", "FromAnotherFile");

        var fileScope = NewFileScope(context, diagnostics);
        fileScope.DeclareTopLevelType("Foo", foo);

        var blockScope = new NamespaceScope(fileScope, "Bar", context.ProgramModel);

        var result = blockScope.Lookup(
            new LookupRequest("FromAnotherFile", MemberVisibility.None, MakeDummyNode()));

        _ = Assert.IsType<NotFoundResult>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lexical shadowing
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A local variable declared in an inner scope shadows a type with the same
    /// name registered in an outer scope. The lookup returns VariableResult and
    /// never reaches the outer scope.
    /// </summary>
    [Fact]
    public void Lookup_VariableInInnerScope_ShadowsTypeInOuterScope()
    {
        var (context, diagnostics) = CreateContext();

        var outerScope = new MemberScope(AssemblyScopeOf(context));
        var typeX = new ClassType("x", "Test", new Scope(null), null, null);
        outerScope.DeclareType("x", typeX);

        var innerScope = new BodyScope(outerScope);
        var varX = new LocalVariable("x", ErrorType.Instance);
        innerScope.DeclareLocal(varX, MakeDummyNode(), diagnostics);

        var result = innerScope.Lookup(new LookupRequest("x", MemberVisibility.InstanceAndStatic, MakeDummyNode()));

        var variable = Assert.IsType<VariableResult>(result);
        Assert.Same(varX, variable.Variable);
    }

    /// <summary>
    /// A local type declared in an inner scope shadows a variable with the same
    /// name registered in an outer scope. The lookup returns TypeResult and
    /// never reaches the outer scope.
    /// </summary>
    [Fact]
    public void Lookup_TypeInInnerScope_ShadowsVariableInOuterScope()
    {
        var (context, diagnostics) = CreateContext();

        var outerScope = new BodyScope(AssemblyScopeOf(context));
        var varX = new LocalVariable("x", ErrorType.Instance);
        outerScope.DeclareLocal(varX, MakeDummyNode(), diagnostics);

        var innerScope = new MemberScope(outerScope);
        var typeX = new ClassType("x", "Test", new Scope(null), null, null);
        innerScope.DeclareType("x", typeX);

        var result = innerScope.Lookup(new LookupRequest("x", MemberVisibility.InstanceAndStatic, MakeDummyNode()));

        var type = Assert.IsType<TypeResult>(result);
        Assert.Same(typeX, type.Type);
    }

    /// <summary>
    /// When nothing lexical matches, the walk keeps going and the namespace levels answer. There is
    /// no second pass: the using level is simply further out than the local one.
    /// </summary>
    [Fact]
    public void Lookup_NothingLexical_ReachesNamespaceLevels()
    {
        var (context, diagnostics) = CreateContext();

        var foo = RegisterType(context.ProgramModel, "NsA", "Foo");

        var scope = NewFileScope(context, diagnostics);
        scope.DeclareImport("NsA");

        var result = scope.Lookup(new LookupRequest("Foo", MemberVisibility.InstanceAndStatic, MakeDummyNode()));

        var type = Assert.IsType<TypeResult>(result);
        Assert.Same(foo, type.Type);
    }

    /// <summary>
    /// Running out of chain is a miss, not an error.
    /// </summary>
    [Fact]
    public void Lookup_NothingAnywhere_ReturnsNotFound()
    {
        var (context, diagnostics) = CreateContext();

        var scope = NewFileScope(context, diagnostics);

        var result = scope.Lookup(
            new LookupRequest("DoesNotExist", MemberVisibility.InstanceAndStatic, MakeDummyNode()));

        _ = Assert.IsType<NotFoundResult>(result);
    }
}