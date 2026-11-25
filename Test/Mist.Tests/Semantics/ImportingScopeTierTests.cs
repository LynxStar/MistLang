using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class ImportingScopeTierTests
{
    private const string Ambiguous = "MIST0299";

    [Fact]
    public void AnAmbiguityAmongDeclaredImportsIsNotSettledByAProjectedOne()
    {
        var imports = new Imports();
        _ = imports.Declared("NsA", "Foo");
        _ = imports.Projected("System", "Foo");
        _ = imports.Declared("NsB", "Foo");
        var file = imports.File("NsA", "System", "NsB");

        var found = file.Lookup(new LookupRequest("Foo", MemberVisibility.None, HandBuiltModel.Node()));

        Assert.Same(ErrorType.Instance, Assert.IsType<TypeResult>(found).Type);
        Assert.Equal(
            "The type name 'Foo' is ambiguous between imported namespaces 'NsA' and 'NsB'. Use a fully-qualified name to disambiguate.",
            Assert.Single(imports.Diagnostics.ToImmutableArray()).Message);
    }

    [Fact]
    public void ADeclaredMatchLeavesAProjectedAmbiguityUnreported()
    {
        var imports = new Imports();
        _ = imports.Projected("System", "Timer");
        _ = imports.Projected("System.Threading", "Timer");
        var declared = imports.Declared("Standard", "Timer");
        var file = imports.File("System", "System.Threading", "Standard");

        var found = file.Lookup(new LookupRequest("Timer", MemberVisibility.None, HandBuiltModel.Node()));

        Assert.Same(declared, Assert.IsType<TypeResult>(found).Type);
        Assert.Empty(imports.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAmbiguityAnswersTheErrorTypeAndIsReportedOnlyAtANode(bool withNode)
    {
        var imports = new Imports();
        _ = imports.Projected("System", "Timer");
        _ = imports.Projected("System.Threading", "Timer");
        var file = imports.File("System", "System.Threading");
        var node = withNode ? HandBuiltModel.Node(offset: 7, path: "imports.mist") : null;

        var found = file.Lookup(new LookupRequest("Timer", MemberVisibility.None, node));

        Assert.Same(ErrorType.Instance, Assert.IsType<TypeResult>(found).Type);
        var diagnostics = imports.Diagnostics.ToImmutableArray();
        if(withNode)
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(Ambiguous, diagnostic.Number.Value);
            Assert.Equal("imports.mist", diagnostic.Location.Path);
            Assert.Contains("'System' and 'System.Threading'", diagnostic.Message);
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    private sealed class Imports
    {
        private readonly ProgramModelClass model = new();
        private readonly MistAssembly self;

        public Imports()
        {
            self = model.AddSelfAssembly("Probe");
        }

        public DiagnosticBag Diagnostics { get; } = new();

        public ClassType Declared(string namespaceName, string name)
        {
            return Register(namespaceName, name, self);
        }

        public ClassType Projected(string namespaceName, string name)
        {
            return Register(namespaceName, name, MistAssembly.Ambient);
        }

        public FileScope File(params string[] usings)
        {
            var file = new FileScope(new Scope(null), model, Diagnostics);
            foreach(var name in usings)
            {
                file.DeclareImport(name);
            }

            return file;
        }

        private ClassType Register(string namespaceName, string name, MistAssembly assembly)
        {
            var type = new ClassType(name, namespaceName, new Scope(null), null, null) { Assembly = assembly };
            Assert.True(model.RegisterType(type));
            return type;
        }
    }
}