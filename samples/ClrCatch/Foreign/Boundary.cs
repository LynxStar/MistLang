namespace ClrCatchFixture;

public sealed class Boundary
{
    public static readonly InvalidOperationException Failure = new("foreign failure");
    public static readonly BoundaryCancellation Cancellation = new();
    public static string Trace { get; private set; } = "";

    public static void Reset()
    {
        Trace = "";
    }

    public static Boundary Receiver()
    {
        Trace += "receiver;";
        return new Boundary();
    }

    public static Func<string, int> Callable()
    {
        Trace += "callable;";
        return Invoke;
    }

    public static (string, string) Arguments()
    {
        Trace += "pack;";
        return ("left", "right");
    }

    public static string Argument()
    {
        Trace += "argument;";
        return "fail";
    }

    public static string FailingArgument()
    {
        Trace += "argument;";
        throw Failure;
    }

    public int Member(string value)
    {
        return Invoke(value);
    }

    public static int Invoke(string value)
    {
        Trace += "invoke;";
        return value == "ok" ? 7 : throw Failure;
    }

    public static Task<int> SynchronousTaskFailure()
    {
        throw Failure;
    }

    public static async Task<int> FaultLater()
    {
        await Task.Yield();
        throw Failure;
    }

    public static async Task<int> CancelLater()
    {
        await Task.Yield();
        throw Cancellation;
    }

    public static async Task CompleteLater()
    {
        await Task.Yield();
    }

    public static IEnumerable<int> Deferred()
    {
        yield return 1;
        throw Failure;
    }

    public static int Advance(IEnumerable<int> items)
    {
        return items.Sum();
    }

    public static LateResource CreateResource()
    {
        return new LateResource();
    }

    public static string WriteFile()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "mist");
        return path;
    }

    public static void RemoveFile(string path)
    {
        File.Delete(path);
    }

    public static string State<T>(Task<T> task)
    {
        return task.Status.ToString();
    }

    public static void Finish()
    {
        Trace += "void;";
    }
}

public sealed class BoundaryCancellation : OperationCanceledException
{
    public BoundaryCancellation() : base("boundary cancellation")
    {
    }
}

public sealed class LateResource : IDisposable
{
    public void Dispose()
    {
        _ = Boundary.Invoke("fail");
    }
}