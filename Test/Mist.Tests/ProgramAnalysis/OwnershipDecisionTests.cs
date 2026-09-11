// Tier 3 (Component): what order 11's rules deliberately do NOT report -- the moves, leases, lends,
// receivers, disciplines, readonly contexts and captures classic admits -- and the reach facts the
// corpus cannot phrase. WHY each is silent, and which of them classic refuses, is recorded once, on
// order 11 of backlog/analysis-v2-capability-progression.yaml.

using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

public sealed class OwnershipDecisionTests : ProgramModelProbe
{
    private static readonly string[] Ids =
    [
        "MIST_PA_USE_AFTER_MOVE", "MIST_PA_LOOP_MOVE", "MIST_PA_LEASE_VIOLATION", "MIST_PA_INCOMPATIBLE_LEND",
        "MIST_PA_THIS_TIER_MISMATCH", "MIST_PA_THIS_TIER_INSUFFICIENT", "MIST_PA_SHARED_EXTRACT",
        "MIST_PA_OWNED_ABSTRACT", "MIST_PA_OWNED_SUBSTRUCTURAL_BINDING", "MIST_PA_CONFLICTING_DISCIPLINE",
        "MIST_PA_FIELD_DISCIPLINE_CONTAINMENT", "MIST_PA_FIELD_TYPE_PARAMETER_UNCONSTRAINED",
        "MIST_PA_REF_ON_CLASS", "MIST_PA_REF_STRUCT_FIELD", "MIST_PA_REF_STRUCT_TYPE_ARGUMENT",
        "MIST_PA_FIELD_ASSIGNED_IN_READONLY_MEMBER", "MIST_PA_FIELD_ASSIGNED_ON_READONLY_PARAMETER",
        "MIST_PA_READONLY_FIELD_ASSIGNED", "MIST_PA_STACK_SPAN_RETURNED", "MIST_PA_CAPTURED_BINDING",
    ];

    public OwnershipDecisionTests()
        : base("MistOwnership_")
    {
    }

    protected override string Fixtures { get; } = """
        public owned class Engine
        {
            public int Rpm;

            public Engine(int rpm)
            {
                this.Rpm = rpm;
            }

            public void Rev(int by)
            {
                this.Rpm = this.Rpm + by;
            }

            public int Get()
            {
                return Rpm;
            }
        }

        public class Widget
        {
            public int Value;

            public Widget(int value)
            {
                this.Value = value;
            }

            public void SetValue(int v)
            {
                this.Value = v;
            }

            public int GetValue()
            {
                return Value;
            }
        }

        public class Account
        {
            public owned int Balance;
            public int Plain;

            public Account(owned int balance)
            {
                this.Balance = balance;
                this.Plain = 0;
            }

            public void Deposit(owned int amount)
            {
                this.Balance = this.Balance + amount;
            }

            public void DepositTwice()
            {
                Deposit(1);
                Deposit(1);
            }

            public void BumpPlain()
            {
                this.Plain = this.Plain + 1;
            }

            public int Read()
            {
                return Balance;
            }

            public string Close() with owned
            {
                return "closed";
            }
        }

        public class Car
        {
            public owned Engine Front;
            public Engine Spare;
            public Account Book;

            public Car()
            {
                this.Front = new Engine(1);
                this.Spare = new Engine(2);
                this.Book = new Account(1);
            }

            public void Service()
            {
                Front.Rev(1);
                this.Front.Rev(1);
                this.Spare.Rev(1);
                this.Book.Deposit(1);
            }
        }

        public struct Vec2
        {
            public int X;

            public Vec2(int x)
            {
                this.X = x;
            }
        }

        public linear class Handle
        {
            public int Fd;

            public Handle(int fd)
            {
                this.Fd = fd;
            }

            public void Drop()
            {
            }

            public int Get()
            {
                return Fd;
            }
        }

        public relevant class Resource
        {
            public void Drop()
            {
            }

            public int Peek()
            {
                return 1;
            }
        }

        public linear class Session
        {
            public Handle Held;
            public Resource Shared;
            public Task<int> Work;

            public Session(Handle h, Resource r, Task<int> w)
            {
                this.Held = h;
                this.Shared = r;
                this.Work = w;
            }

            public void Drop()
            {
            }
        }

        public linear class LinearBox<T> where T : linear
        {
            public T Value;

            public LinearBox(T value)
            {
                this.Value = value;
            }

            public void Drop()
            {
            }
        }

        public class Counter
        {
            public int Count;
            public readonly int Id;

            public Counter(int id)
            {
                this.Id = id;
                this.Count = 0;
            }

            public readonly Counter(int id, int count)
            {
                this.Id = id;
                this.Count = count;
            }

            public void Increment()
            {
                this.Count = this.Count + 1;
            }

            public readonly int Peek()
            {
                return Count;
            }

            public readonly int Twice()
            {
                return Peek() * 2;
            }

            public void Safe()
            {
                readonly
                {
                    this.Count = 2;
                }
            }
        }

        public ref struct Cursor
        {
            public int Pos;

            public Cursor(int pos)
            {
                this.Pos = pos;
            }
        }

        public ref struct Window
        {
            public Cursor Start;

            public Window(Cursor start)
            {
                this.Start = start;
            }
        }

        public class Keeper
        {
            public Engine Field;

            public Keeper()
            {
                this.Field = new Engine(0);
            }

            public void Store(owned Engine e)
            {
                this.Field = e;
                Console.WriteLine(e.Get());
            }
        }

        public class Helpers
        {
            public static void Consume(owned Engine e) { Console.WriteLine(e.Get()); }
            public static void Borrow(borrowed Engine e) { e.Rev(1); }
            public static void Freeze(frozen Engine e) { Console.WriteLine(e.Get()); }
            public static void View(readonly ref Engine e) { Console.WriteLine(e.Get()); }
            public static void Relend(borrowed Engine e) { View(e); Freeze(e); Borrow(e); Consume(e); }
            public static void Lift(frozen Engine e) { Freeze(e); View(e); Consume(e); }
            public static void Hand(readonly ref Engine e) { View(e); Consume(e); }
            public static void Borrow2(borrowed Vec2 v) { Console.WriteLine(v.X); }
            public static void Freeze2(frozen Vec2 v) { Console.WriteLine(v.X); }
            public static void Take(Handle h) { Console.WriteLine(h.Get()); }
            public static void Read(Account a) { Console.WriteLine(a.Read()); }
            public static void Bump(Account a) { a.BumpPlain(); }
            public static void Twice(Account a) { a.DepositTwice(); }
            public static Account Make() { return new Account(1); }
            public static Cursor Advance(Cursor c) { return new Cursor(c.Pos + 1); }
            public static int Sum(Span<int> s) { return s[0]; }
        
        }
        """;

    protected override string Preamble { get; } = """
        var flag = true;
        """;

    [Theory]
    [InlineData("owned var e = new Engine(1); owned var f = e; Console.WriteLine(f.Get());")]
    [InlineData("owned var e = new Engine(1); owned var f = e; e = new Engine(3); Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); if(flag) { Helpers.Consume(e); } else { Helpers.Consume(e); }")]
    [InlineData("owned var e = new Engine(1); if(flag) { Helpers.Consume(e); } else { Console.WriteLine(e.Get()); }")]
    [InlineData("owned var e = new Engine(1); if(flag) { Helpers.Consume(e); return; } Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); if(flag) { Helpers.Consume(e); } e = new Engine(2); Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); while(flag) { Helpers.Consume(e); e = new Engine(2); flag = false; } Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); while(flag) { Helpers.Borrow(e); flag = false; } Console.WriteLine(e.Get());")]
    [InlineData("var e = new Engine(1); var f = e; Console.WriteLine(e.Get());")]
    [InlineData("var e = new Engine(1); Helpers.Consume(e); Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); e.Rpm = 5; Helpers.Consume(e);")]
    [InlineData("owned var e = new Engine(1); Console.WriteLine(e.Get()); owned var f = e;")]
    [InlineData("owned var v = new Vec2(1); owned var w = v; Console.WriteLine(v.X);")]
    [InlineData("var k = new Keeper(); k.Store(new Engine(1));")]
    [InlineData("var f = new Handle(1); Helpers.Take(f); Console.WriteLine(f.Get());")]
    [InlineData("var r = new Resource(); r.Drop(); Console.WriteLine(r.Peek());")]
    [InlineData("var r = new Resource(); var alias = r; Console.WriteLine(r.Peek() + alias.Peek());")]
    [InlineData("owned var e = new Engine(1); Console.WriteLine(e.Get());")]
    public void AMoveClassicAdmits_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("owned var e = new Engine(1); frozen var f = e; Console.WriteLine(e.Get()); Console.WriteLine(f.Get());")]
    [InlineData("owned var e = new Engine(1); frozen var f = e; frozen var g = e; Console.WriteLine(f.Get() + g.Get());")]
    [InlineData("owned var e = new Engine(1); frozen var f = e; Helpers.Borrow(e); Console.WriteLine(f.Get());")]
    [InlineData("owned var e = new Engine(1); readonly ref var r = e; e.Rev(1); Console.WriteLine(r.Get());")]
    [InlineData("owned var e = new Engine(1); { frozen var f = e; Console.WriteLine(f.Get()); } e.Rpm = 2;")]
    [InlineData("owned var e = new Engine(1); { borrowed var b = e; Console.WriteLine(b.Get()); } Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); while(flag) { frozen var f = e; Console.WriteLine(f.Get()); break; } e.Rev(1);")]
    [InlineData("owned var e = new Engine(1); if(flag) { borrowed var b = e; b.Rev(1); return; } Console.WriteLine(e.Get());")]
    [InlineData("owned var e = new Engine(1); Helpers.View(e); Helpers.Freeze(e); e.Rev(1); Helpers.Borrow(e); Console.WriteLine(e.Get());")]
    [InlineData("var e = new Engine(1); Helpers.View(e);")]
    [InlineData("var e = new Engine(1); Helpers.Consume(e);")]
    [InlineData("owned var e = new Engine(1); Helpers.Relend(e);")]
    [InlineData("owned var e = new Engine(1); Helpers.Lift(e);")]
    [InlineData("owned var e = new Engine(1); Helpers.Hand(e);")]
    [InlineData("var v = new Vec2(1); Helpers.Borrow2(v); Helpers.Freeze2(v); Console.WriteLine(v.X);")]
    public void ALeaseOrLendClassicAdmits_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("owned var w = new Widget(1); w.SetValue(2);")]
    [InlineData("var a = new Account(1); a.Deposit(1); var closed = a.Close();")]
    [InlineData("owned var a = new Account(1); a.Deposit(1); a.Deposit(1); Console.WriteLine(a.Read());")]
    [InlineData("Helpers.Read(new Account(1));")]
    [InlineData("Helpers.Bump(new Account(1));")]
    [InlineData("Helpers.Twice(new Account(1));")]
    [InlineData("var car = new Car(); car.Service(); car.Front.Rev(1); car.Spare.Rev(1);")]
    [InlineData("new Account(1).Deposit(1);")]
    [InlineData("Helpers.Make().Deposit(1);")]
    [InlineData("owned var car = new Car(); owned var f = car.Front; Console.WriteLine(car.Front.Get());")]
    [InlineData("owned var car = new Car(); var f = car.Front; Console.WriteLine(car.Front.Get());")]
    public void AReceiverClassicAdmits_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("var s = new Session(new Handle(1), new Resource(), Task.FromResult(1));")]
    [InlineData("var b = new LinearBox<Handle>(new Handle(1));")]
    [InlineData("var c = new Counter(1); c.Increment(); c.Safe(); Console.WriteLine(c.Twice());")]
    [InlineData("var c = new Counter(1, 2); Console.WriteLine(c.Peek());")]
    [InlineData("var w = new Window(new Cursor(1)); Console.WriteLine(w.Start.Pos);")]
    [InlineData("Cursor c = new Cursor(1); Console.WriteLine(Helpers.Advance(c).Pos);")]
    [InlineData("Span<int> s = stackalloc int[4]; Console.WriteLine(s[0]); Console.WriteLine(Helpers.Sum(s));")]
    [InlineData("var e = new Engine(1); func<int, int> f = x => x + e.Get(); Console.WriteLine(f(1));")]
    [InlineData("owned var e = new Engine(1); func<int, int> f = static x => x + 1; Console.WriteLine(f(1));")]
    public void ADeclarationOrContextClassicAdmits_IsAdmitted(string statement)
    {
        AssertSilent(statement);
    }

    [Theory]
    [InlineData("owned var e = new Engine(1); Helpers.Consume(e); Console.WriteLine(e.Get() + e.Get());")]
    [InlineData("owned var e = new Engine(1); borrowed var b = e; Console.WriteLine(e.Get()); Console.WriteLine(e.Get()); b.Rev(1);")]
    [InlineData("owned var e = new Engine(1); while(flag) { Helpers.Consume(e); flag = false; }")]
    public void AFindingIsReportedOnce(string statement)
    {
        _ = Assert.Single(Analyse(statement));
    }

    [Theory]
    [InlineData("Console.WriteLine(nothing.Get());")]
    [InlineData("owned var e = new Engine(1); Helpers.Consume(e); Console.WriteLine(nothing);")]
    public void AValueThatAlreadyFailed_IsNotReportedAgain(string statement)
    {
        AssertSilent(statement);
    }

    // ── Harness ─────────────────────────────────────────────────────────

    private void AssertSilent(string statement)
    {
        Assert.Empty(Analyse(statement));
    }

    private List<Diagnostic> Analyse(string statement)
    {
        var model = BuildModel(statement, extraTypes: "", extraPreamble: "", out _);
        var findings = new DiagnosticBag();

        ProgramAnalysisRunner.Run(model, findings);

        return [.. findings.ToImmutableArray().Where(d => Ids.Contains(d.Id.Value))];
    }
}