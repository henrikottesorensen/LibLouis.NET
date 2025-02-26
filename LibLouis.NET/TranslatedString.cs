namespace LibLouis.NET;

public class TranslatedString
{
    public required string Translated { get; set; }
    
    public required int[] OutputPosition { get; set; }
    
    public required int[] InputPosition { get; set; }
    
    public required int CursorPosition { get; set; }
}
