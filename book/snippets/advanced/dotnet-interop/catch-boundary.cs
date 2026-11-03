using Standard;
using System;
using System.IO;

namespace CatchBoundaryExample;

public class Program
{
    public static Standard.Result__Standard_String__Standard_Error Read(string path)
    {
        var __e1309 = (((System.Func<(System.Func<string, string>, string), Standard.Result__Standard_String__System_Exception>)((__e1310) =>
{
    try
    {
        return Standard.Result__Standard_String__System_Exception.Success(__e1310.Item1(__e1310.Item2));
    }
    catch (System.Exception __e1311)
    {
        return Standard.Result__Standard_String__System_Exception.Failure(__e1311);
    }
}
))((System.IO.File.ReadAllText, path))).Inspect();
        if (__e1309.IsTuple_2_Boolean_Exception)
        {
            return Standard.Result__Standard_String__Standard_Error.Failure(((Standard.ExceptionError)(__e1309.AsTuple_2_Boolean_Exception.Item2)));
        }
        return Standard.Result__Standard_String__Standard_Error.Success(__e1309.AsTuple_2_Boolean_String.Item2);
    }

    public static void Main()
    {
        System.Console.WriteLine(Read("").Inspect() switch { { AsTuple_2_Boolean_StringOrNull: (true, var text) } => text, { AsTuple_2_Boolean_ErrorOrNull: (false, var error) } => error.Message, var _ => "unreachable" });
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
