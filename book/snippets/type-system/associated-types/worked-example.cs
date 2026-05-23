using Standard;
using System;

namespace AssociatedTypesSnippet;

interface IStringable
{
    string Stringify();
}
interface IProducer<TOutput>
    where TOutput : IStringable
{
    string ProduceValue();
}
interface IIterator<TCursor>
    where TCursor : struct
{
    int GetFirst();
    int GetSecond();
}
class MessageProducer : IProducer<MessageProducer.Output>
{
    public class Output : IStringable
    {
        public string Value;

        public Output(string value)
        {
            Value = value;
        }

        public string Stringify()
        {
            return $"Output({Value})";
        }

        protected Output()
        {
            this.Value = "";
        }

        public static Output __CreateDefault()
        {
            var inst = new Output();
            return inst;
        }
    }

    public string ProduceValue()
    {
        return "hello from producer";
    }

    public Output CreateOutput(string msg)
    {
        return new Output(msg);
    }

    public static MessageProducer __CreateDefault()
    {
        var inst = new MessageProducer();
        return inst;
    }
}
class RangeIterator : IIterator<RangeIterator.Cursor>
{
    public struct Cursor
    {
        public int Position;

        public Cursor(int pos)
        {
            Position = pos;
        }

        public static Cursor __CreateDefault()
        {
            var inst = new Cursor();
            inst.Position = 0;
            return inst;
        }
    }

    public int GetFirst()
    {
        return 10;
    }

    public int GetSecond()
    {
        return 20;
    }

    public Cursor CreateCursor(int pos)
    {
        return new Cursor(pos);
    }

    public static RangeIterator __CreateDefault()
    {
        var inst = new RangeIterator();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        var producer = new MessageProducer();
        var output = producer.CreateOutput("hello");
        System.Console.WriteLine(output.Stringify());
        var iter = new RangeIterator();
        var cursor = iter.CreateCursor(42);
        System.Console.WriteLine($"cursor.Position={cursor.Position}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
