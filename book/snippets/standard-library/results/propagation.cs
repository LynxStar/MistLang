using Standard;

namespace ResultExample;

public class Program
{
    public static Standard.Result__Standard_Int32__Standard_Error Read(bool fail)
    {
        if (fail)
        {
            return Standard.Result__Standard_Int32__Standard_Error.Failure(new Standard.MessageError("read failed"));
        }
        return Standard.Result__Standard_Int32__Standard_Error.Success(41);
    }

    public static Standard.Result__Standard_Int32__Standard_Error Next(bool fail)
    {
        var __e1 = (Read(fail)).Inspect();
        if (__e1.IsTuple_2_Boolean_Error)
        {
            return Standard.Result__Standard_Int32__Standard_Error.Failure(__e1.AsTuple_2_Boolean_Error.Item2);
        }
        return Standard.Result__Standard_Int32__Standard_Error.Success(__e1.AsTuple_2_Boolean_Int32.Item2 + 1);
    }

    public static void Main()
    {
        System.Console.WriteLine(Next(false).Inspect() switch { { AsTuple_2_Boolean_Int32OrNull: (true, var value) } => System.FormattableString.Invariant($"value={value}"), { AsTuple_2_Boolean_ErrorOrNull: (false, var error) } => error.Message, var _ => "unreachable" });
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
