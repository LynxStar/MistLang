using System;

namespace Snippet;

public class Result<T>
{
    private T value;
    private bool success;

    public Result(T value, bool success)
    {
        this.value = value;
        this.success = success;
    }

    public bool IsSuccess()
    {
        return success;
    }
}

public class Program
{
    public static Snippet.Result<Standard.Void> SuccessResult()
    {
        return new Snippet.Result<Standard.Void>(Standard.Void.Instance, true);
    }

    public static void Main(string[] args)
    {
        Snippet.Result<Standard.Void> success = SuccessResult();
        Console.WriteLine($"IsSuccess: {success.IsSuccess()}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
