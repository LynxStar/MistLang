using System;
using System.Text.RegularExpressions;

var numbers = new[] { 10, 20, 30, 40, 50 };
var trimmed = numbers[1..^(1)];
var lastThree = numbers[^(3)..];
Console.WriteLine($"trimmed={trimmed.Length}");
Console.WriteLine($"trimmedFirst={trimmed[0]}");
Console.WriteLine($"lastThree={lastThree.Length}");
Console.WriteLine($"lastThreeFirst={lastThree[0]}");
