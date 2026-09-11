using System;

namespace Snippet;

public class AppState
{
    public int ErrorCount;
    public bool Ready;
    public string Status;

    public AppState()
    {
        this.ErrorCount = 5;
        this.Ready = true;
        this.Status = "initialized";
    }

    public static AppState __CreateDefault()
    {
        return new AppState();
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        Snippet.AppState state = Snippet.AppState.__CreateDefault();
        Console.WriteLine($"ErrorCount={state.ErrorCount} Ready={state.Ready} Status={state.Status}");
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
