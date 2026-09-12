using Standard;

System.Text.RegularExpressions.Regex digits = new System.Text.RegularExpressions.Regex("\\d+", System.Text.RegularExpressions.RegexOptions.Compiled);
System.Console.WriteLine($"pattern={digits.ToString()}");
System.Console.WriteLine($"matches_123={digits.IsMatch("123")}");
System.Console.WriteLine($"matches_abc={digits.IsMatch("abc")}");
