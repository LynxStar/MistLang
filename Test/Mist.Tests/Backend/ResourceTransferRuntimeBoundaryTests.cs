using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mist.Backends.CSharpTranspiler;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

/// <summary>
/// The boundary between the emitted releases and the runtime: each body runs once on the path its value
/// is handed on and once on the path where something fails first -- a later argument, an absent
/// receiver, the callee after entry, the body of a with or a for, a Drop's own code -- and every value
/// it held must be released exactly once on both. A Mist program cannot catch, so only a harness that
/// does can watch these paths.
/// </summary>
[Trait("Tier", "4")]
public sealed class ResourceTransferRuntimeBoundaryTests
{
    private readonly LifetimeFragments f = new();

    [Fact]
    public void EmittedReleasesRunExactlyOnceOnEveryPathAtTheRuntimeBoundary()
    {
        var source = Harness();
        var compilation = CSharpCompilation.Create("ResourceTransferBoundary" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)], CSharpCompilationDriver.CoreReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics) + "\n" + source);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning);
        stream.Position = 0;
        var context = new AssemblyLoadContext("ResourceTransferBoundary", isCollectible: true);

        try
        {
            var assembly = context.LoadFromStream(stream);
            var execute = assembly.GetType("Probe.Harness")!.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;

            Assert.Equal(
                "trailing:1/1 receiver:2/2 conditional:3/3 entered:4/4 with:5/5 for:6/6 owned:7/7/7/7 lease:8/8 lent:9 drop:10,11/10,11",
                execute.Invoke(null, null));
        }
        finally
        {
            context.Unload();
        }
    }

    private string Harness()
    {
        var take = new Parameter("take", f.Boolean);
        var owned = Owned("t", f.Token);
        var box = new Parameter("b", f.Box);
        var lease = f.Local("l", f.Lease);
        f.Lease.ConstructorsList.Add(new Constructor("Lease", f.Lease, [new Parameter("id", f.Int32)], default) { DeclaringType = f.Lease });
        var newLease = new NewExpression(HandBuiltModel.Node(), f.Lease, [HandBuiltModel.Literal(f.Int32, 8)], f.Lease.ConstructorsList[0], [], [f.Int32]);
        var pair = Static("Pair", new Parameter("t", f.Token), new Parameter("xs", f.Int32));
        var thrown = Static("TakeThenThrow", new Parameter("t", f.Token));
        var always = HandBuiltModel.Literal(f.Boolean, true);

        var trailing = Body(Declare(Local("t"), f.New(1)),
            Run(f.Call(pair, Ref(Local("t")), new ArrayLiteralExpression(HandBuiltModel.Node(), f.Int32, [f.Call(f.Risky)], f.Int32))));
        var receiver = Body([box], Declare(Local("t"), f.New(2)), Run(f.CallOn(Ref(box), conditional: false, Ref(Local("t")))));
        var conditional = Body([box], Declare(Local("t"), f.New(3)), Run(f.CallOn(Ref(box), conditional: true, Ref(Local("t")))));
        var entered = Body(Declare(Local("t"), f.New(4)), Run(f.Call(thrown, Ref(Local("t")))));
        var scoped = Body(Declare(Local("t"), f.New(5)), With(Ref(Local("t")), HandBuiltModel.Block(Run(f.Call(f.Risky)))));
        var looped = Body(new ForStatement(Declare(Local("t"), f.New(6)), always, null, HandBuiltModel.Block(Run(f.Call(f.Risky)), new BreakStatement())));
        var owning = Body([owned, take], new IfStatement(Ref(take), HandBuiltModel.Block(Run(DropOf(Ref(owned)))), null), Run(f.Call(f.Risky)));
        var leasing = Body([take], Declare(lease, newLease), new IfStatement(Ref(take), HandBuiltModel.Block(Run(DropOf(Ref(lease)))), null), Run(f.Call(f.Risky)));
        var lending = Body(Run(f.Call(f.Peek, f.New(9))), Run(f.Call(f.Risky)));
        var dropping = Emitted(
            f.HolderDrop(HandBuiltModel.Block(new IfStatement(Ref(take), HandBuiltModel.Block(Run(DropOf(Ref(f.First)))), null), Run(f.Call(f.Risky)))),
            f.Holder);
        var ownership = new CSharpWriter();
        LifetimeLowering.Emit(ownership);
        var ambient = new CSharpWriter();
        ContextLowering.Emit(ambient);

        return $$"""
            using System;
            using System.Collections.Generic;
            namespace Probe;
            {{ownership}}
            {{ambient}}
            public sealed class Token
            {
                public int Id;
                public Token(int id) { Id = id; }
                public void Drop() { Harness.Released.Add(Id); }
            }
            public ref struct Lease
            {
                public int Id;
                public Lease(int id) { Id = id; }
                public void Drop() { Harness.Released.Add(Id); }
            }
            public sealed class Box
            {
                public void Use(Token t) { t.Drop(); }
            }
            public sealed class Holder
            {
                public Token First = new Token(10);
                public Token Second = new Token(11);
                public void Drop(bool take)
                {
                    {{dropping}}
                }
            }
            public static class Sink
            {
                public static bool Fail;
                public static int Risky() { if (Fail) { throw new InvalidOperationException("risky"); } return 0; }
                public static void Pair(Token t, Int32[] xs) { t.Drop(); }
                public static void TakeThenThrow(Token t) { try { throw new InvalidOperationException("callee"); } finally { t.Drop(); } }
                public static void Peek(Token t) { }
                public static void Trailing() { {{trailing}} }
                public static void Receiver(Box b) { {{receiver}} }
                public static void Conditional(Box b) { {{conditional}} }
                public static void Entered() { {{entered}} }
                public static void Scoped() { {{scoped}} }
                public static void Looped() { {{looped}} }
                public static void Owning(Token t, Boolean take) { {{owning}} }
                public static void Leasing(Boolean take) { {{leasing}} }
                public static void Lending() { {{lending}} }
            }
            public static class Harness
            {
                public static readonly List<int> Released = new();
                private static string Run(bool fail, Action body)
                {
                    Released.Clear();
                    Sink.Fail = fail;
                    try { body(); } catch (Exception) { }
                    return string.Join(",", Released);
                }
                public static string Execute()
                {
                    return string.Join(" ", new[]
                    {
                        "trailing:" + Run(true, Sink.Trailing) + "/" + Run(false, Sink.Trailing),
                        "receiver:" + Run(false, () => Sink.Receiver(null)) + "/" + Run(false, () => Sink.Receiver(new Box())),
                        "conditional:" + Run(false, () => Sink.Conditional(null)) + "/" + Run(false, () => Sink.Conditional(new Box())),
                        "entered:" + Run(false, Sink.Entered) + "/" + Run(true, Sink.Entered),
                        "with:" + Run(true, Sink.Scoped) + "/" + Run(false, Sink.Scoped),
                        "for:" + Run(true, Sink.Looped) + "/" + Run(false, Sink.Looped),
                        "owned:" + Run(true, () => Sink.Owning(new Token(7), true)) + "/" + Run(true, () => Sink.Owning(new Token(7), false))
                            + "/" + Run(false, () => Sink.Owning(new Token(7), true)) + "/" + Run(false, () => Sink.Owning(new Token(7), false)),
                        "lease:" + Run(true, () => Sink.Leasing(false)) + "/" + Run(false, () => Sink.Leasing(true)),
                        "lent:" + Run(true, Sink.Lending),
                        "drop:" + Run(true, () => new Holder().Drop(true)) + "/" + Run(false, () => new Holder().Drop(false)),
                    });
                }
            }
            """;
    }

    private readonly Dictionary<string, LocalVariable> locals = [];

    private LocalVariable Local(string name)
    {
        if(!locals.TryGetValue(name, out var local))
        {
            local = f.Local(name);
            locals[name] = local;
        }

        return local;
    }

    private string Body(params Statement[] statements)
    {
        return Body([], statements);
    }

    private string Body(Parameter[] parameters, params Statement[] statements)
    {
        locals.Clear();
        return Emitted(f.Function("Probe", HandBuiltModel.Block(statements), parameters));
    }

    private static string Emitted(Method method, ProgramModel.Model.Types.ConcreteType? owner = null)
    {
        var writer = new CSharpWriter();
        BodyEmitter.ResetTemporaries();
        BodyEmitter.EmitMethodBody(method, method.Body!, writer, new Mist.Common.Diagnostics.DiagnosticBag(), owner: owner);
        return writer.ToString();
    }

    private Method Static(string name, params Parameter[] parameters)
    {
        return new Method(name, f.Void, [.. parameters], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
    }
}