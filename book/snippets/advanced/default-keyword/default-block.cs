using System;

namespace Snippet;

public class AppState
{
    public int ErrorCount;

    public bool Ready;

    public string Status;


    protected AppState()
    {
        this.ErrorCount = 5;
        this.Ready = true;
        this.Status = "initialized";
    }

    public static AppState __CreateDefault()
    {
        var inst = new AppState();
        return inst;
    }
}
public class Program
{
    public static void Main(string[] args)
    {
        AppState state = AppState.__CreateDefault();
        Console.WriteLine($"ErrorCount={state.ErrorCount} Ready={state.Ready} Status={state.Status}");
    }

    public static Program __CreateDefault()
    {
        var inst = new Program();
        return inst;
    }
}
