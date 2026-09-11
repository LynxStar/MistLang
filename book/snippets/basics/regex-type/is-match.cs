using System;

System.Text.RegularExpressions.Regex wordPattern = new System.Text.RegularExpressions.Regex("\\bcat\\b", System.Text.RegularExpressions.RegexOptions.Compiled);
Console.WriteLine($"cat_in_the_cat_sat={wordPattern.IsMatch("the cat sat")}");
Console.WriteLine($"cat_in_concatenate={wordPattern.IsMatch("concatenate")}");
Console.WriteLine($"cat_in_cat={wordPattern.IsMatch("cat")}");
