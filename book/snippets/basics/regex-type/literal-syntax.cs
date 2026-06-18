using System;
using System.Text.RegularExpressions;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
var digits = new System.Text.RegularExpressions.Regex("\\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"pattern={digits.ToString()}");
Console.WriteLine($"matches_123={digits.IsMatch("123")}");
Console.WriteLine($"matches_abc={digits.IsMatch("abc")}");
