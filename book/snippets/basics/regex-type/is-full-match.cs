using Standard;

System.Text.RegularExpressions.Regex fullDigits = new System.Text.RegularExpressions.Regex("\\d{3}", System.Text.RegularExpressions.RegexOptions.Compiled);
System.Console.WriteLine($"fullmatch_123={System.Text.RegularExpressions.Regex.IsMatch("123", "^(?:" + fullDigits.ToString() + ")$")}");
System.Console.WriteLine($"fullmatch_1234={System.Text.RegularExpressions.Regex.IsMatch("1234", "^(?:" + fullDigits.ToString() + ")$")}");
System.Console.WriteLine($"fullmatch_12={System.Text.RegularExpressions.Regex.IsMatch("12", "^(?:" + fullDigits.ToString() + ")$")}");
System.Console.WriteLine($"fullmatch_abc={System.Text.RegularExpressions.Regex.IsMatch("abc", "^(?:" + fullDigits.ToString() + ")$")}");
