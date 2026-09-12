using Standard;

static void DoNothing()
{
    return;
}
DoNothing();
Standard.Void result = Standard.Void.Instance;
System.Console.WriteLine($"DoNothing() returned: {result}");
