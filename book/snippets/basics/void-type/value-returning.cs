public static MistVoid DoNothing()
{
    return MistVoid.Instance;
}

// Return value is captured:
var result = DoNothing();
Console.WriteLine($"DoNothing() returned: {result}");
