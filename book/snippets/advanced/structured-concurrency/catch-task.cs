using Standard;
using System;

namespace CatchTaskExample;

public class Program
{
    public static async System.Threading.Tasks.Task<Standard.Result__Standard_Int32__Standard_Error> DelaySafely(int milliseconds)
    {
        var __e1 = (await ((System.Func<(System.Func<int, System.Threading.Tasks.Task>, int), System.Threading.Tasks.Task<Standard.Result__Standard_Void__System_Exception>>)(async (__e2) =>
{
    try
    {
        await __e2.Item1(__e2.Item2);
        return Standard.Result__Standard_Void__System_Exception.Success(Standard.Void.Instance);
    }
    catch (System.Exception __e3)
    {
        return Standard.Result__Standard_Void__System_Exception.Failure(__e3);
    }
}
))((System.Threading.Tasks.Task.Delay, milliseconds))).Inspect();
        if (__e1.IsTuple_2_Boolean_Exception)
        {
            return Standard.Result__Standard_Int32__Standard_Error.Failure(((Standard.ExceptionError)(__e1.AsTuple_2_Boolean_Exception.Item2)));
        }
        return Standard.Result__Standard_Int32__Standard_Error.Success(1);
    }

    public static async System.Threading.Tasks.Task Main()
    {
        System.Console.WriteLine((await DelaySafely(0)).Inspect() switch { { AsTuple_2_Boolean_Int32OrNull: (true, var value) } => System.FormattableString.Invariant($"completed={value}"), { AsTuple_2_Boolean_ErrorOrNull: (false, var error) } => error.Message, var _ => "unreachable" });
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
