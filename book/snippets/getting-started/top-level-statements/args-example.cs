using Standard;

System.Console.WriteLine(System.FormattableString.Invariant($"Received {args.Length} arguments"));
if (args.Length > 0)
{
    System.Console.WriteLine($"First argument: {args[0]}");
}
