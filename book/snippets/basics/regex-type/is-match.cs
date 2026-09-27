using Standard;

System.Text.RegularExpressions.Regex wordPattern = new System.Text.RegularExpressions.Regex("\\bcat\\b", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
System.Console.WriteLine($"cat_in_the_cat_sat={wordPattern.IsMatch("the cat sat")}");
System.Console.WriteLine($"cat_in_concatenate={wordPattern.IsMatch("concatenate")}");
System.Console.WriteLine($"cat_in_cat={wordPattern.IsMatch("cat")}");
