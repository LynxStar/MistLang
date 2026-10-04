using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Accessibility = Mist.ProgramModel.Model.Enums.Accessibility;

namespace Mist.Tests.Backend;

[Trait("Tier", "4")]
public sealed class AsyncPromotionRuntimeBoundaryTests
{
    [Fact]
    public async Task EmittedAdaptersCompleteDelayedWorkAndPreserveEvaluationAtTheRuntimeBoundary()
    {
        var fixture = new Fixture();
        var source = fixture.Emit();
        var compilation = CSharpCompilation.Create("AsyncPromotionBoundary" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        stream.Position = 0;
        var context = new AssemblyLoadContext("AsyncPromotionBoundary", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            var execute = assembly.GetType("Probe.Harness")!.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;
            var pending = Assert.IsAssignableFrom<Task<string>>(execute.Invoke(null, null));
            Assert.Equal("direct,lift,group,group,abstract,extension,optional,drop;captures=1;absent=0", await pending.WaitAsync(TimeSpan.FromSeconds(20)));
        }
        finally
        {
            context.Unload();
        }
    }

    private sealed class Fixture
    {
        private readonly HandBuiltModel built = new();
        private readonly StructType integer;
        private readonly ClassType nothing;
        private readonly ClassType task;
        private readonly ClassType reader = HandBuiltModel.Class("Reader");
        private readonly AbstractType contract = new("IReader", "Probe", new Scope(null), null, null);
        private readonly Method read;
        private readonly Method dropWork;
        private readonly ExtensionBlock extension;

        public Fixture()
        {
            integer = new StructType("Int32", "Standard", new Scope(null), null, null);
            nothing = built.Type("Void");
            task = new ClassType("Task<>", "Standard", new Scope(null), null, null)
            { Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Relevant } };
            task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
            _ = built.Model.RegisterType(task);
            var helpers = HandBuiltModel.Class("Helpers");
            var taskInteger = TaskOf(integer);
            var fetch = new Method("Fetch", taskInteger, [], new MemberModifiers { IsAsync = true, IsStatic = true })
            { DeclaringType = helpers };
            var fetched = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), helpers), "Fetch", taskInteger),
                [], taskInteger, fetch, []);
            var drop = new Method("Drop", integer, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task };
            var awaited = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), fetched, "Drop", integer),
                [], integer, drop, []);
            read = new Method("Read", integer, [], Public())
            { DeclaringType = reader, Body = HandBuiltModel.Block(new ReturnStatement(awaited, integer)) };
            reader.MethodsList.Add(read);
            contract.MethodsList.Add(new Method("Read", integer, [], Public()) { DeclaringType = contract });
            reader.BaseAbstractTypes.Add(contract);
            var pending = new LocalVariable("pending", taskInteger);
            dropWork = new Method("DropWork", nothing, [], Public())
            { DeclaringType = reader, Body = HandBuiltModel.Block(new LocalDeclarationStatement(pending, fetched)) };
            reader.MethodsList.Add(dropWork);
            built.Model.TaskDrops.RecordDrop(read);
            var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
            unit.Types.Add(reader);
            built.Model.AddSelfAssembly("Probe").CompilationUnits.Add(unit);
            extension = new ExtensionBlock("self", "Probe", new Scope(null), null)
            { ResolvedReceiverType = integer, CarrierName = "IntExtensions" };
            var extensionMethod = new Method("ReadExtension", integer, [], Public())
            { Body = HandBuiltModel.Block(new ReturnStatement(awaited, integer)) };
            extension.MethodsList.Add(extensionMethod);
            unit.Extensions.Add(extension);
            built.Model.TaskDrops.RecordDrop(extensionMethod);
        }

        public string Emit()
        {
            using var promotion = AsyncPromotion.Enter(built.Model);
            BodyEmitter.ResetTemporaries();
            var diagnostics = new DiagnosticBag();
            var methods = new CSharpWriter();
            MethodEmitter.Emit(read, false, methods, diagnostics, reader);
            MethodEmitter.Emit(dropWork, false, methods, diagnostics, reader);
            var any = false;
            AsyncSlotBridgeEmitter.Emit(reader, methods, diagnostics, ref any);
            var extensions = new CSharpWriter();
            any = false;
            ExtensionBlockEmitter.Emit(extension, extensions, diagnostics, ref any);
            var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("reader", reader));
            var direct = Call(receiver, read, integer);
            var lift = new AsyncExpression(HandBuiltModel.Node(), direct, TaskOf(integer), isLift: true);
            var next = new Method("Next", reader, [], Public() with { IsStatic = true }) { DeclaringType = reader };
            var nextCall = new InvocationExpression(HandBuiltModel.Node(), new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), reader), "Next", reader), [], reader, next, []);
            var group = new FunctionReferenceExpression(HandBuiltModel.Node(), nextCall, read, new FunctionType([], integer));
            var optional = built.Interner.GetOrCreateUnionType([reader, nothing]);
            var optionalInteger = built.Interner.GetOrCreateUnionType([integer, nothing]);
            var maybeMethod = new Method("Maybe", optional, [], Public() with { IsStatic = true }) { DeclaringType = reader };
            var maybe = new InvocationExpression(HandBuiltModel.Node(),
                new MemberAccessExpression(HandBuiltModel.Node(), new TypeReferenceExpression(HandBuiltModel.Node(), reader), "Maybe", optional),
                [], optional, maybeMethod, []);
            var conditional = Call(maybe, read, optionalInteger, isConditional: true);
            var directText = BodyEmitter.Render(direct, diagnostics);
            var liftText = BodyEmitter.Render(lift, diagnostics);
            var groupText = BodyEmitter.Render(group, diagnostics);
            var conditionalText = BodyEmitter.Render(conditional, diagnostics);
            Assert.Empty(diagnostics.ToImmutableArray());

            return $$"""
                namespace Probe;
                public interface IReader { int Read(); }
                public sealed class Reader : IReader
                {
                    public static int Captures;
                    public static int MaybeCalls;
                    public static Reader OptionalTarget;
                    public static Reader Maybe() { MaybeCalls++; return OptionalTarget; }
                    public static Reader Next() { Captures++; return new Reader(); }
                    {{methods}}
                }
                {{extensions}}
                public static class Helpers
                {
                    public static System.Threading.Tasks.TaskCompletionSource<int> Gate;
                    public static System.Threading.Tasks.TaskCompletionSource<bool> Started;
                    public static int Calls;
                    public static int Completed;
                    public static void Reset()
                    {
                        Gate = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                        Started = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                        Calls = 0;
                        Completed = 0;
                    }
                    public static async System.Threading.Tasks.Task<int> Fetch()
                    {
                        System.Threading.Interlocked.Increment(ref Calls);
                        Started.SetResult(true);
                        var value = await Gate.Task.ConfigureAwait(false);
                        System.Threading.Interlocked.Increment(ref Completed);
                        return value;
                    }
                }
                public static class Harness
                {
                    private static async System.Threading.Tasks.Task Check(System.Func<System.Threading.Tasks.Task<int>> action)
                    {
                        Helpers.Reset();
                        var pending = action();
                        await Helpers.Started.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
                        if(pending.IsCompleted) throw new System.InvalidOperationException("returned before completion");
                        Helpers.Gate.SetResult(7);
                        if(await pending != 7 || Helpers.Calls != 1 || Helpers.Completed != 1)
                            throw new System.InvalidOperationException("lost or duplicated work");
                    }
                    private static async System.Threading.Tasks.Task<int> Direct(Reader reader) { return {{directText}}; }
                    private static async System.Threading.Tasks.Task<int?> Optional(Reader maybe) { Reader.OptionalTarget = maybe; return {{conditionalText}}; }
                    public static async System.Threading.Tasks.Task<string> Execute()
                    {
                        var reader = new Reader();
                        await Check(() => Direct(reader));
                        await Check(() => {{liftText}});
                        var read = {{groupText}};
                        if(Reader.Captures != 1) throw new System.InvalidOperationException("receiver capture count");
                        await Check(() => System.Threading.Tasks.Task.Run(read));
                        await Check(() => System.Threading.Tasks.Task.Run(read));
                        IReader view = reader;
                        await Check(() => System.Threading.Tasks.Task.Run(() => view.Read()));
                        await Check(() => 2.ReadExtension());
                        Helpers.Reset();
                        var absent = await Optional(null);
                        if(absent.HasValue || Helpers.Calls != 0 || Reader.MaybeCalls != 1) throw new System.InvalidOperationException("absent invocation");
                        await Check(async () => (await Optional(reader)).Value);
                        Helpers.Reset();
                        var dropped = reader.DropWork();
                        await Helpers.Started.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
                        if(dropped.IsCompleted) throw new System.InvalidOperationException("orphaned drop");
                        Helpers.Gate.SetResult(7);
                        await dropped;
                        if(Helpers.Completed != 1 || Helpers.Calls != 1 || Reader.Captures != 1 || Reader.MaybeCalls != 2)
                            throw new System.InvalidOperationException("completion accounting");
                        return "direct,lift,group,group,abstract,extension,optional,drop;captures=1;absent=0";
                    }
                }
                """;
        }

        private IMistType TaskOf(IMistType result)
        {
            return built.Interner.GetOrCreateConstructedType(task, [result]);
        }

        private static MemberModifiers Public()
        {
            return new MemberModifiers { Accessibility = Accessibility.Public };
        }

        private static InvocationExpression Call(Expression receiver, Method method, IMistType result, bool isConditional = false)
        {
            return new InvocationExpression(HandBuiltModel.Node(),
                new MemberAccessExpression(HandBuiltModel.Node(), receiver, method.Name, result, isConditional: isConditional),
                [], result, method, []);
        }
    }
}