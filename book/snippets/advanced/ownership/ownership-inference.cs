using Standard;

namespace Snippet;

public class Counter
{
    public int Value;

    public Counter(int value)
    {
        this.Value = value;
    }

    public void Increment()
    {
        this.Value = this.Value + 1;
    }

    public string Describe()
    {
        return $"Counter({Value})";
    }
}

public class OwnershipInferenceDemo
{
    public static void Run()
    {
        Snippet.Counter counter = new Snippet.Counter(0);
        counter.Increment();
        System.Console.WriteLine($"inferred owned={counter.Describe()}");
        string label = "hello";
        string alias = label;
        System.Console.WriteLine($"label={label}");
        System.Console.WriteLine($"alias={alias}");
    }

    public OwnershipInferenceDemo()
    {
    }

    public static OwnershipInferenceDemo __CreateDefault()
    {
        return new OwnershipInferenceDemo();
    }
}
