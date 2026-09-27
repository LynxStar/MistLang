using Standard;

System.Text.RegularExpressions.Regex letters = new System.Text.RegularExpressions.Regex("[a-zA-Z]+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
System.Console.WriteLine($"pattern={letters.ToString()}");
System.Console.WriteLine($"matches_Hello={letters.IsMatch("Hello")}");
System.Console.WriteLine($"matches_123={letters.IsMatch("123")}");
