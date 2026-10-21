// Tier 4: emitted context keys and scope cleanup preserve their meaning through Roslyn and runtime.

using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Integration;

[Trait("Tier", "4")]
public sealed class StructuredContextRuntimeBoundaryTests
{
    [Fact]
    public async Task ModelToEmitterToRoslynToRuntime_KeepsStructuredStacksSeparateAndRestoresThemOnEveryExit()
    {
        var model = new HandBuiltModel();
        var text = model.Type("String");
        var integer = model.Type("Int32");
        var nothing = model.Type("Void");
        var optionalText = new UnionType([text, nothing]);
        var plainStep = new FunctionType([integer], integer);
        var pureStep = new FunctionType([integer], integer, isPure: true);
        var plainTuple = new TupleType([plainStep, integer]);
        var pureTuple = new TupleType([pureStep, integer]);

        Statement call(string name, bool awaited = false)
        {
            var task = model.Type("Task<>");
            var result = awaited ? (IMistType)new ConstructedType(task, [nothing]) : nothing;
            var reference = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(name, new FunctionType([], result)));
            var invocation = new InvocationExpression(HandBuiltModel.Node(), reference, [], result, null, []);
            if(!awaited)
            {
                return new ExpressionStatement(invocation);
            }

            var drop = new Method("Drop", nothing, [], new MemberModifiers { IsIntrinsic = true }) { DeclaringType = task };
            var access = new MemberAccessExpression(HandBuiltModel.Node(), invocation, "Drop", nothing);
            return new ExpressionStatement(new InvocationExpression(HandBuiltModel.Node(), access, [], nothing, drop, []));
        }

        WithBlockStatement under(IMistType key, Expression value, params Statement[] statements)
        {
            return new WithBlockStatement(value, HandBuiltModel.Block(statements), keyType: key);
        }

        TupleLiteralExpression tuple(string parameter, FunctionType step, TupleType tuple)
        {
            return new TupleLiteralExpression(HandBuiltModel.Node(),
                [new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable(parameter, step)), HandBuiltModel.Literal(integer, 1)],
                [null, null], tuple);
        }

        Block outer(params Statement[] statements)
        {
            return HandBuiltModel.Block(under(text, HandBuiltModel.Literal(text, "outer"),
                under(optionalText, HandBuiltModel.Literal(text, "optional"),
                    under(plainTuple, tuple("plainStep", plainStep, plainTuple),
                        under(pureTuple, tuple("pureStep", pureStep, pureTuple), statements)))));
        }

        var normal = outer(
            call("Record"),
            under(text, HandBuiltModel.Literal(text, "inner"),
                under(optionalText, new CastExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(optionalText),
                    optionalText, [], optionalText), call("Record"))),
            call("Record"),
            call("CatchAndRecord"));
        var throwing = HandBuiltModel.Block(under(text, HandBuiltModel.Literal(text, "boom"),
            under(optionalText, HandBuiltModel.Literal(text, "boom-opt"), call("Record"), call("Raise"))));
        var throwingOuter = outer(call("Raise"));
        var worker = HandBuiltModel.Block(under(text,
            new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("name", text)),
            under(optionalText, new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("optional", optionalText)),
                under(text, HandBuiltModel.Literal(text, "nested"), call("Observe")),
                call("Observe"), call("HoldAndObserve", awaited: true))));
        var concurrent = outer(call("RunChildren", awaited: true));
        var consumers = new[] { (Name: "ReadText", Key: (IMistType)text), ("ReadOptionalText", optionalText),
            ("ReadPlainTuple", plainTuple), ("ReadPureTuple", pureTuple) };
        var methods = string.Join("\n", consumers.Select(consumer => Consumer(consumer.Name, consumer.Key)));
        var runtime = new CSharpWriter();
        ContextLowering.Emit(runtime);
        var source = $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public static class Probe
            {
                private static readonly System.Collections.Generic.List<string> observations = new();
                {{methods}}
                private static void Normal(System.Func<int, int> plainStep, System.Func<int, int> pureStep)
                {
                    {{Emit(normal, "plainStep", "pureStep")}}
                }
                private static void Throwing()
                {
                    {{Emit(throwing)}}
                }
                private static void ThrowingOuter(System.Func<int, int> plainStep, System.Func<int, int> pureStep)
                {
                    {{Emit(throwingOuter, "plainStep", "pureStep")}}
                }
                private static void Record()
                {
                    observations.Add(ReadText() + "|" + (ReadOptionalText() ?? "<null>")
                        + "|" + (ReadPlainTuple().Item1(5) + ReadPlainTuple().Item2)
                        + "|" + (ReadPureTuple().Item1(5) + ReadPureTuple().Item2));
                }
                private static void Raise() { throw new System.InvalidOperationException("scope failure"); }
                private static void CatchAndRecord()
                {
                    try { Throwing(); }
                    catch(System.InvalidOperationException error) when(error.Message == "scope failure") { Record(); }
                }
                private static bool Empty(System.Action read)
                {
                    try { read(); return false; }
                    catch(System.Collections.Generic.KeyNotFoundException) { return true; }
                    catch(System.InvalidOperationException) { return true; }
                }
                private static string AllEmpty()
                {
                    return Empty(() => { ReadText(); }) && Empty(() => { ReadOptionalText(); })
                        && Empty(() => { ReadPlainTuple(); }) && Empty(() => { ReadPureTuple(); })
                        ? "empty" : "leaked";
                }
                private static string Snapshot()
                {
                    return ReadText() + "|" + ReadOptionalText()
                        + "|" + (ReadPlainTuple().Item1(5) + ReadPlainTuple().Item2)
                        + "|" + (ReadPureTuple().Item1(5) + ReadPureTuple().Item2);
                }
                private static async Task<string> ReadAfterAwait()
                {
                    await Task.Yield();
                    return Snapshot();
                }
                private static async Task<string[]> Worker(string name, TaskCompletionSource ready, Task release, bool fail)
                {
                    var values = new System.Collections.Generic.List<string> { Snapshot() };
                    string? optional = name + "?";
                    void Observe() { values.Add(Snapshot()); }
                    async Task HoldAndObserve()
                    {
                        ready.SetResult();
                        await release.WaitAsync(TimeSpan.FromSeconds(5));
                        values.Add(await ReadAfterAwait());
                        if (fail) { Raise(); }
                    }
                    try
                    {
                        {{Emit(worker, "name", "optional")}}
                    }
                    catch(InvalidOperationException error) when(error.Message == "scope failure") { }
                    values.Add(Snapshot());
                    return values.ToArray();
                }
                private static async Task RunChildren()
                {
                    var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    var first = Task.Run(() => Worker("first", firstReady, releaseFirst.Task, false));
                    await firstReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var second = Task.Run(() => Worker("second", secondReady, releaseSecond.Task, true));
                    await secondReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    observations.Add("parent-overlap:" + Snapshot());
                    releaseFirst.SetResult();
                    observations.AddRange(await first.WaitAsync(TimeSpan.FromSeconds(5)));
                    observations.Add("parent-after-first:" + Snapshot());
                    releaseSecond.SetResult();
                    observations.AddRange(await second.WaitAsync(TimeSpan.FromSeconds(5)));
                    observations.Add("parent-after-second:" + Snapshot());
                    observations.Add(await Task.Run(() =>
                    {
                        MistContext.Pop(typeof(string));
                        var empty = Empty(() => ReadText());
                        MistContext.Push(typeof(string), "replacement");
                        return "child-pop:" + empty + "|" + MistContext.Peek(typeof(string));
                    }));
                    observations.Add("parent-after-pop:" + Snapshot());
                    Task<string> suppressed;
                    using(ExecutionContext.SuppressFlow())
                    {
                        suppressed = Task.Run(async () =>
                        {
                            var empty = AllEmpty();
                            MistContext.Push<string>("isolated");
                            await Task.Yield();
                            var value = ReadText();
                            MistContext.Pop<string>();
                            return "suppressed:" + empty + "|" + value + "|" + AllEmpty();
                        });
                    }
                    observations.Add(await suppressed.WaitAsync(TimeSpan.FromSeconds(5)));
                    observations.Add("parent-after-suppressed:" + Snapshot());
                }
                private static async Task Concurrent(System.Func<int, int> plainStep, System.Func<int, int> pureStep)
                {
                    {{Emit(concurrent, "plainStep", "pureStep")}}
                }
                public static async Task<string[]> Run()
                {
                    Normal(value => value + 10, value => value + 100);
                    observations.Add(AllEmpty());
                    try { ThrowingOuter(value => value + 10, value => value + 100); }
                    catch(System.InvalidOperationException error) when(error.Message == "scope failure") { }
                    observations.Add(AllEmpty());
                    await Concurrent(value => value + 10, value => value + 100);
                    observations.Add(AllEmpty());
                    return observations.ToArray();
                }
            }
            """;
        var parse = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create("StructuredContextRuntime_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(runtime.ToString(), parse, path: "__Context.g.cs"), CSharpSyntaxTree.ParseText(source, parse, path: "Probe.cs")],
            CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        Assert.DoesNotContain(emitted.Diagnostics, diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
        stream.Position = 0;
        var context = new AssemblyLoadContext("StructuredContextRuntime", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            var actual = await Assert.IsAssignableFrom<Task<string[]>>(assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null));
            Assert.Equal(["outer|optional|16|106", "inner|<null>|16|106", "outer|optional|16|106",
                "boom|boom-opt|16|106", "outer|optional|16|106", "empty", "empty",
                "parent-overlap:outer|optional|16|106",
                "outer|optional|16|106", "nested|first?|16|106", "first|first?|16|106", "first|first?|16|106", "outer|optional|16|106",
                "parent-after-first:outer|optional|16|106",
                "outer|optional|16|106", "nested|second?|16|106", "second|second?|16|106", "second|second?|16|106", "outer|optional|16|106",
                "parent-after-second:outer|optional|16|106", "child-pop:True|replacement", "parent-after-pop:outer|optional|16|106",
                "suppressed:empty|isolated|empty", "parent-after-suppressed:outer|optional|16|106", "empty"], actual);
        }
        finally
        {
            context.Unload();
        }
    }

    private static string Consumer(string name, IMistType key)
    {
        var local = new LocalVariable("value", key) { IsContextBound = true };
        var method = new Method(name, key, [], new MemberModifiers { IsStatic = true })
        {
            Body = HandBuiltModel.Block(new ReturnStatement(new LocalReferenceExpression(HandBuiltModel.Node(), local), key)),
        };
        method.ContextBindingsList.Add(new ContextBinding(key, local.Name));
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        writer.WriteLine($"private static {TypeNameEmitter.Emit(key)} {name}()");
        using(writer.Block())
        {
            BodyEmitter.EmitMethodBody(method, method.Body, writer, diagnostics);
        }

        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }

    private static string Emit(Block body, params string[] parameterNames)
    {
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitBlock(body, parameterNames, writer, diagnostics);
        Assert.False(diagnostics.HasErrors);
        return writer.ToString();
    }
}