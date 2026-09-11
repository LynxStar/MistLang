using System;

static string DescribeVoid(Standard.Void v)
{
    return "received void";
}
string desc = DescribeVoid(Standard.Void.Instance);
Console.WriteLine($"DescribeVoid: {desc}");
