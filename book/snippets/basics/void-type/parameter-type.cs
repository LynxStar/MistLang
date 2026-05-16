public static string DescribeVoid(MistVoid v)
{
    return "received void";
}

// Calling the method:
string desc = DescribeVoid(MistVoid.Instance);
Console.WriteLine($"DescribeVoid: {desc}");
