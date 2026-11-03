using Standard;

Union_String_String_Seat_X0B8611B3 ReadSeat(object input)
{
    return input switch { string and var __e1309 and var seat when System.Text.RegularExpressions.Regex.IsMatch(__e1309, "\\A(?:" + "[A-Z][1-9]" + "(?#\n(?#))\\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant) => Union_String_String_Seat_X0B8611B3.FromString_Seat_X0B8611B3(seat), var _ => Union_String_String_Seat_X0B8611B3.FromString("Expected a seat such as A7") };
}
Snippet.Record_2<string, int> selected = new Snippet.Record_2<string, int>(("A", 7));
string selectedLabel = selected.Map<string>((string row, int number) => System.FormattableString.Invariant($"{row}{number}"));
System.Console.WriteLine((ReadSeat(selectedLabel)).Payload);
Snippet.Record_3<string, int, int> adjacent = new Snippet.Record_3<string, int, int>(("A", 7, 1));
string adjacentLabel = adjacent.Map<string>((string row, int number, int offset) => System.FormattableString.Invariant($"{row}{number + offset}"));
System.Console.WriteLine((ReadSeat(adjacentLabel)).Payload);
System.Console.WriteLine((ReadSeat("AC")).Payload);
System.Console.WriteLine((ReadSeat(7)).Payload);
