var fullDigits = new System.Text.RegularExpressions.Regex("\\d{3}", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"fullmatch_123={System.Text.RegularExpressions.Regex.IsMatch("123", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_1234={System.Text.RegularExpressions.Regex.IsMatch("1234", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_12={System.Text.RegularExpressions.Regex.IsMatch("12", "^(?:\\d{3})$")}");
Console.WriteLine($"fullmatch_abc={System.Text.RegularExpressions.Regex.IsMatch("abc", "^(?:\\d{3})$")}");
