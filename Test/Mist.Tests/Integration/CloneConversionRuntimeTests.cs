using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Integration;

/// <summary>
/// Tier 4, the boundary from emitted C# to the runtime: a newtype's conversions reach private state through
/// UnsafeAccessors the runtime binds by signature only when the call runs. An accessor declared on a closed
/// generic, or one whose name or signature misses its target, compiles and throws there, so only running the
/// conversions tells a working accessor from one that is not.
/// </summary>
[Trait("Tier", "4")]
public sealed class CloneConversionRuntimeTests : IDisposable
{
    private const string ConvertsEachKindOfConstructedSource = """
        using Standard;

        namespace Constructed;

        public struct Pair<A, B>
        {
            private A first;
            public B Second { get; set; }

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }

            public A First()
            {
                return first;
            }
        }

        public class Ledger<T>
        {
            private T serial;

            public Ledger(T serial)
            {
                this.serial = serial;
            }

            public T Serial()
            {
                return serial;
            }
        }

        public readonly class Money<T> : Ledger<T>
        {
            private string currency;

            public Money(string currency, T serial) : base(serial)
            {
                this.currency = currency;
            }

            public string Currency()
            {
                return currency;
            }
        }

        public class Cell<T> : IClonable<Cell<T>>
        {
            private static int made = 0;
            private T content;
            private string label { get; set; }

            public Cell(T content)
            {
                this.content = content;
                this.label = "fresh";
            }

            private Cell(T content, string label)
            {
                this.content = content;
                this.label = label;
            }

            public owned Cell<T> Clone()
            {
                made = made + 1;
                return new Cell<T>(content, $"{label}#{made}");
            }

            public string Describe()
            {
                return $"{content} {label} made={made}";
            }
        }

        public type IntPair = new Pair<int, string>;
        public type IntMoney = new Money<int>;
        public type IntCell = new Cell<int>;

        public class Program
        {
            public static void Main()
            {
                Pair<int, string> left = new IntPair(1, "one");
                IntPair back = (IntPair)new Pair<int, string>(2, "two");
                Money<int> spent = new IntMoney("EUR", 7);
                IntMoney earned = (IntMoney)new Money<int>("USD", 8);
                var cell = new Cell<int>(3);
                var entered = (IntCell)cell;
                var returned = (Cell<int>)entered;

                Console.WriteLine($"{left.First()},{left.Second} {back.First()},{back.Second}");
                Console.WriteLine($"{spent.Currency()},{spent.Serial()} {earned.Currency()},{earned.Serial()}");
                Console.WriteLine($"{entered.Describe()} | {returned.Describe()}");
            }
        }
        """;

    private const string ReachesEveryKindOfPrivateMember = """
        using Standard;

        namespace Reached;

        public abstract IShow
        {
            string Show();
        }

        public class Word : IShow
        {
            private string text;

            public Word(string text)
            {
                this.text = text;
            }

            public string Show()
            {
                return text;
            }
        }

        public class Tiger : IClonable<Tiger>
        {
            private int stripes;
            private string? note;

            private int Doubled
            {
                get { return stripes * 2; }
                set { stripes = value / 2; }
            }

            private string? Note
            {
                get { return note; }
                set { note = value; }
            }

            private int this[int index]
            {
                get { return stripes + index; }
                set { stripes = value - index; }
            }

            public Tiger(int stripes)
            {
                this.stripes = stripes;
            }

            private T Echo<T>(T value)
            {
                return value;
            }

            private int Three()
            {
                return 3;
            }

            private void Bump()
            {
                stripes = stripes + 100;
            }

            private static int Seven()
            {
                return 7;
            }

            private string Shown<U>(U item) where U : IShow
            {
                return item.Show();
            }

            public owned Tiger Clone()
            {
                var copy = new Tiger(Echo<int>(stripes));
                Tiger? maybe = copy;
                int? viaConditional = maybe?.stripes;
                Doubled = Doubled + 2;
                copy.Note ??= copy.Shown<Word>(new Word("noted"));
                Note ??= Shown<Word>(new Word("self"));
                copy[1] = this[1] + 10;
                func<int> three = Three;
                func<int> seven = Seven;
                func<int> copyThree = copy.Three;
                maybe?.Bump();
                int? maybeThree = maybe?.Three();
                Bump();
                int conditional = viaConditional ?? 0;
                int fromMaybe = maybeThree ?? 0;
                copy.stripes = copy.stripes + three() + conditional + copy.Echo<int>(1) + seven() + copyThree() + fromMaybe;
                return copy;
            }

            public string Describe()
            {
                return $"stripes={stripes} note={note}";
            }
        }

        public type Jaguar = new Tiger;

        public class Program
        {
            public static void Main()
            {
                var tiger = new Tiger(4);
                var jaguar = (Jaguar)tiger;
                var back = (Tiger)jaguar;

                Console.WriteLine($"{tiger.Describe()} | {jaguar.Describe()} | {back.Describe()}");
            }
        }
        """;

    private const string ReachesASourceEmittedPerInstantiation = """
        using Standard;

        namespace Specialized;

        public readonly class Keep<K>
        {
            private K? item;

            public Keep(K? start)
            {
                this.item = start;
            }

            public string Describe()
            {
                if (this.item is K present)
                {
                    return $"present {present}";
                }

                return "absent";
            }
        }

        public class Held<K> : IClonable<Held<K>>
        {
            private K? item;
            private int copies;

            public Held(K? start)
            {
                this.item = start;
            }

            private Held(K? start, int copies)
            {
                this.item = start;
                this.copies = copies;
            }

            public owned Held<K> Clone()
            {
                return new Held<K>(this.item, this.copies + 1);
            }

            public string Describe()
            {
                if (this.item is K present)
                {
                    return $"present {present} copies {copies}";
                }

                return $"absent copies {copies}";
            }
        }

        public type IntKeep = new Keep<int>;
        public type IntHeld = new Held<int>;

        public class Program
        {
            public static void Main()
            {
                int? none = void;
                var keep = (IntKeep)new Keep<int>(7);
                Keep<int> kept = keep;
                var held = (IntHeld)new Held<int>(none);
                Held<int> back = (Held<int>)held;

                Console.WriteLine($"{keep.Describe()} | {kept.Describe()} | {held.Describe()} | {back.Describe()}");
            }
        }
        """;

    private const string ConvertsEachKindOfGenericNewtype = """
        using Standard;

        namespace Probe;

        public readonly class Held<T>
        {
            public T Item { get; }
            private string label;

            public Held(T item, string label)
            {
                this.Item = item;
                this.label = label;
            }

            public string Describe()
            {
                return $"{label}={Item}";
            }
        }

        public struct Pair<A, B>
        {
            private A first;
            public B Second { get; set; }

            public Pair(A first, B second)
            {
                this.first = first;
                this.Second = second;
            }

            public string Describe()
            {
                return $"({first}, {Second})";
            }
        }

        public abstract IShow
        {
            string Show();
        }

        public class Word : IShow
        {
            private string text;

            public Word(string text)
            {
                this.text = text;
            }

            public string Show()
            {
                return text;
            }
        }

        public class Cell<T> : IClonable<Cell<T>> where T : IShow
        {
            private static int made = 0;
            private T content;
            private int reads;

            public Cell(T content)
            {
                this.content = content;
                this.reads = 0;
            }

            private Cell(T content, int reads)
            {
                this.content = content;
                this.reads = reads;
            }

            public owned Cell<T> Clone()
            {
                made = made + 1;
                var copy = new Cell<T>(content, reads);
                Cell<T>? maybe = copy;
                int? read = maybe?.reads;
                copy.reads = (read ?? 0) + 1;
                return copy;
            }

            public string Describe()
            {
                return $"{content.Show()} reads={reads} made={made}";
            }
        }

        public readonly class Keep<K>
        {
            private K? item;

            public Keep(K? start)
            {
                this.item = start;
            }

            public string Describe()
            {
                if (this.item is K present)
                {
                    return $"kept {present}";
                }

                return "kept nothing";
            }
        }

        public class Slot<K> : IClonable<Slot<K>>
        {
            private K? item;
            private int copies { get; set; }

            public Slot(K? start)
            {
                this.item = start;
                this.copies = 0;
            }

            private Slot(K? start, int copies)
            {
                this.item = start;
                this.copies = copies;
            }

            public owned Slot<K> Clone()
            {
                return new Slot<K>(this.item, copies + 1);
            }

            public string Describe()
            {
                if (this.item is K present)
                {
                    return $"slot {present} copies {copies}";
                }

                return $"empty slot copies {copies}";
            }
        }

        public class Shelf<U>
        {
            public type Own = new Held<U>;
            public type Kept<V> = new Pair<V, U>;

            public static string Both(U item)
            {
                var own = (Own)new Held<U>(item, "own");
                Held<U> back = own;
                var kept = (Kept<int>)new Pair<int, U>(1, item);
                Pair<int, U> pair = kept;
                return $"{own.Describe()} {back.Describe()} {kept.Describe()} {pair.Describe()}";
            }
        }

        public class Rack
        {
            private type Own<K> = new Pair<K, int>;

            public class Beside
            {
            }

            public static string Both()
            {
                var own = (Own<string>)new Pair<string, int>("r", 2);
                Pair<string, int> back = own;
                return $"{own.Describe()} {back.Describe()}";
            }
        }

        public type Kept<X> = new Held<X>;
        public type Swapped<X, Y> = new Pair<Y, X>;
        public type Celled<C> = new Cell<C>;
        public type Kept2<X> = new Keep<X>;
        public type Slotted<Y> = new Slot<Y>;

        public class Program
        {
            public static void Main(string[] args)
            {
                var numbers = (Kept<int>)new Held<int>(4, "n");
                var words = (Kept<string>)new Held<string>("w", "s");
                Held<int> backNumbers = numbers;
                var backWords = (Held<string>)words;
                Console.WriteLine($"{numbers.Describe()} {words.Describe()} {backNumbers.Describe()} {backWords.Describe()}");

                var swapped = (Swapped<int, string>)new Pair<string, int>("one", 1);
                var flipped = (Swapped<string, int>)new Pair<int, string>(2, "two");
                Pair<string, int> backSwapped = swapped;
                Pair<int, string> backFlipped = flipped;
                Console.WriteLine($"{swapped.Describe()} {flipped.Describe()} {backSwapped.Describe()} {backFlipped.Describe()}");

                var celled = (Celled<Word>)new Cell<Word>(new Word("a"));
                var again = (Celled<Word>)new Cell<Word>(new Word("b"));
                var uncelled = (Cell<Word>)again;
                Console.WriteLine($"{celled.Describe()} | {again.Describe()} | {uncelled.Describe()}");

                int? none = void;
                string? word = "k";
                var keptNone = (Kept2<int>)new Keep<int>(none);
                var keptWord = (Kept2<string>)new Keep<string>(word);
                Keep<string> backKept = keptWord;
                var slottedNone = (Slotted<int>)new Slot<int>(none);
                var slottedWord = (Slotted<string>)new Slot<string>(word);
                var backSlot = (Slot<string>)slottedWord;
                Console.WriteLine($"{keptNone.Describe()} | {keptWord.Describe()} | {backKept.Describe()} | {slottedNone.Describe()} | {slottedWord.Describe()} | {backSlot.Describe()}");

                Console.WriteLine($"{Shelf<string>.Both("u")} | {Shelf<int>.Both(7)}");

        Kept<int>? absent = void;
        Held<int>? stillAbsent = absent;
        var shown = "absent";

        if (stillAbsent is Held<int> present)
        {
            shown = present.Describe();
        }

        Console.WriteLine($"{Rack.Both()} | {shown}");
            }
        }
        """;

    private readonly string scratchDir;

    public CloneConversionRuntimeTests()
    {
        scratchDir = Path.Combine(Path.GetTempPath(), "mist-constructed-clone-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch(IOException) { }
        }

        GC.SuppressFinalize(this);
    }

    private const string ConvertsASelfTypedSlotWholeEitherWay = """
        using Standard;

        namespace SelfTyped;

        public class Node<T> : IClonable<Node<T>>
        {
            public T Item;
            public Node<T>? Next;
            private int depth;

            public Node(T item, Node<T>? next)
            {
                this.Item = item;
                this.Next = next;
                this.depth = 0;
            }

            public int Depth()
            {
                return depth;
            }

            public owned Node<T> Clone()
            {
                if(this.Next is Node<T> next)
                {
                    var copy = new Node<T>(this.Item, next.Clone());
                    copy.depth = next.depth + 1;
                    return copy;
                }

                Node<T>? end = void;
                return new Node<T>(this.Item, end);
            }
        }

        public class Link : IClonable<Link>
        {
            public int Item;
            public Link? Next;

            public Link(int item, Link? next)
            {
                this.Item = item;
                this.Next = next;
            }

            public owned Link Clone()
            {
                return new Link(this.Item, this.Next?.Clone());
            }
        }

        public type Chain<T> = new Node<T>;
        public type IntChain = new Node<int>;
        public type Rope = new Link;

        public class Program
        {
            public static string SpellNode<T>(Node<T>? head)
            {
                var text = "";
                var cursor = head;
                while(cursor is Node<T> here)
                {
                    text = $"{text} {here.Item}/{here.Depth()}";
                    cursor = here.Next;
                }

                return text;
            }

            public static string SpellChain<T>(Chain<T>? head)
            {
                var text = "";
                var cursor = head;
                while(cursor is Chain<T> here)
                {
                    text = $"{text} {here.Item}/{here.Depth()}";
                    cursor = here.Next;
                }

                return text;
            }

            public static string SpellRope(Rope? head)
            {
                var text = "";
                var cursor = head;
                while(cursor is Rope here)
                {
                    text = $"{text} {here.Item}";
                    cursor = here.Next;
                }

                return text;
            }

            public static void Main(string[] args)
            {
                Node<int>? end = void;
                var nodes = new Node<int>(1, new Node<int>(2, new Node<int>(3, end)));

                var chain = (Chain<int>)nodes;
                Console.WriteLine($"in{SpellChain<int>(chain)}");
                Console.WriteLine($"out{SpellNode<int>((Node<int>)chain)}");

                var closed = (IntChain)nodes;
                Console.WriteLine($"closed {closed.Item} {closed.Next is void}");

                if(nodes.Next is Node<int> second)
                {
                    second.Item = 99;
                }

                Console.WriteLine($"apart{SpellChain<int>(chain)} |{SpellNode<int>(nodes)}");

                Link? none = void;
                var rope = (Rope)new Link(4, new Link(5, none));
                var tail = (Rope)new Link(6, none);
                Console.WriteLine($"conditional{SpellRope(rope)} | {tail.Next is void}");
            }
        }
        """;

    [Fact]
    public void EveryConversionOfAConstructedSource_CopiesItsPrivateStateAtRuntime()
    {
        Assert.Equal(
            "1,one 2,two\nEUR,7 USD,8\n3 fresh#1 made=1 | 3 fresh#1#1 made=1",
            CompileAndRun(ConvertsEachKindOfConstructedSource).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AConversionCloningASelfTypedSlot_ConvertsTheWholeChainEitherWay()
    {
        Assert.Equal(
            "in 1/1 2/1 3/0\n"
            + "out 1/2 2/1 3/0\n"
            + "closed 1 False\n"
            + "apart 1/1 2/1 3/0 | 1/0 99/0 3/0\n"
            + "conditional 4 5 | True",
            CompileAndRun(ConvertsASelfTypedSlotWholeEitherWay).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void EveryKindOfPrivateMemberAConversionReaches_RunsThroughItsAccessor()
    {
        Assert.Equal(
            "stripes=105 note=self | stripes=237 note=noted | stripes=400 note=noted",
            CompileAndRun(ReachesEveryKindOfPrivateMember));
    }

    [Fact]
    public void ASourceEmittedPerInstantiation_IsReachedAsTheClosedTypeItIs()
    {
        Assert.Equal(
            "present 7 | present 7 | absent copies 1 | absent copies 2",
            CompileAndRun(ReachesASourceEmittedPerInstantiation));
    }

    [Fact]
    public void EveryConversionOfAGenericNewtype_ReachesItsSourceAtEachInstance()
    {
        Assert.Equal(
            "n=4 s=w n=4 s=w\n"
            + "(one, 1) (2, two) (one, 1) (2, two)\n"
            + "a reads=1 made=2 | b reads=1 made=2 | b reads=2 made=1\n"
            + "kept nothing | kept k | kept k | empty slot copies 1 | slot k copies 1 | slot k copies 2\n"
            + "own=u own=u (1, u) (1, u) | own=7 own=7 (1, 7) (1, 7)\n"
            + "(r, 2) (r, 2) | absent",
            CompileAndRun(ConvertsEachKindOfGenericNewtype).ReplaceLineEndings("\n"));
    }

    private string CompileAndRun(string source)
    {
        var sourcePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(sourcePath, source);

        var driver = new CompilerDriver(new Mist.Lexing.MistLexer(), new Mist.Parsing.MistParser());

        var result = driver.Compile(new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(sourcePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(scratchDir, "out"),
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "Probe",
            NamespaceRoot: "Probe"));

        var errors = result.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"  {d.Id.Value}: {d.Message}")
            .ToList();

        Assert.True(errors.Count == 0, $"Compilation failed:\n{string.Join("\n", errors)}");
        Assert.NotNull(result.PrimaryOutputArtifactPath);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = result.PrimaryOutputArtifactPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(process);

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        _ = process.WaitForExit(60000);

        Assert.True(process.ExitCode == 0, stderr);

        return stdout.Trim();
    }
}