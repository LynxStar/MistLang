using Standard;

namespace ResultArithmeticExample;

public class Program
{
    public static Standard.Result__Standard_Int32__Standard_DivisionError Scaled(int x, int y, int scale)
    {
        var __e1309 = (MistIntrinsics.Int32_op_Multiply(Standard.ResultArithmetic.DivideChecked(x, y), scale)).Inspect();
        if (__e1309.IsTuple_2_Boolean_DivisionError)
        {
            return Standard.Result__Standard_Int32__Standard_DivisionError.Failure(__e1309.AsTuple_2_Boolean_DivisionError.Item2);
        }
        return Standard.Result__Standard_Int32__Standard_DivisionError.Success(__e1309.AsTuple_2_Boolean_Int32.Item2);
    }

    public static void Main()
    {
        int proven = 20 / 4;
        decimal rounded = 1m / 3m;
        Standard.Result__Standard_Int32__Standard_DivisionError uncertain = Scaled(20, 0, 3);
        string description = uncertain.Inspect() switch { { AsTuple_2_Boolean_Int32OrNull: (true, var value) } => System.FormattableString.Invariant($"success={value}"), { AsTuple_2_Boolean_DivisionErrorOrNull: (false, var error) } => error.Message, var _ => "unreachable" };
        System.Console.WriteLine(System.FormattableString.Invariant($"{proven}; {rounded}; {description}"));
    }

    public Program()
    {
    }

    public static Program __CreateDefault()
    {
        return new Program();
    }
}
