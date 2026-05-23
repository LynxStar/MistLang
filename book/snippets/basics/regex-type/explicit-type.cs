using System;
using System.Text.RegularExpressions;

System.Text.RegularExpressions.Regex letters = new System.Text.RegularExpressions.Regex("[a-zA-Z]+", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"pattern={letters.ToString()}");
Console.WriteLine($"matches_Hello={letters.IsMatch("Hello")}");
Console.WriteLine($"matches_123={letters.IsMatch("123")}");
