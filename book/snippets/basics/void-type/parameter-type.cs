using Standard;

static string DescribeVoid(Standard.Void v)
{
    return "received void";
}
string desc = DescribeVoid(Standard.Void.Instance);
System.Console.WriteLine($"DescribeVoid: {desc}");
