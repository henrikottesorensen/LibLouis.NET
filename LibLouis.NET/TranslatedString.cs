namespace LibLouis.NET;

public class TranslatedString
{
    public required string Output { get; set; }
    
    public required int[] OutputPosition { get; set; }
    
    public required int[] InputPosition { get; set; }
    
    public required int CursorPosition { get; set; }
}
