namespace LibLouis.NET;

public enum TranslationMode
{
    Regular = 0,

    /// <summary>
    /// 
    /// </summary>
    noContractions = 1,

    /// <summary>
    /// If this bit is set in the mode parameter the space-bounded characters containing the cursor will be translated in computer braille.
    /// </summary>
    ComputerBrailleAtCursor = 2,

    /// <summary>
    /// When this bit is set, during forward translation, Liblouis will produce output as dot patterns. During back-translation Liblouis accepts input as dot patterns.
    /// Note that the produced dot patterns are affected if you have any display opcode (see display) defined in any of your tables.
    /// </summary>
    DotsIO = 4,
    // for historic reasons 8 and 16 are free

    /// <summary>
    /// If this bit is set, only the characters to the left of the cursor will be in computer braille. This bit overrides compbrlAtCursor.

    /// </summary>
    ComputerBrailleLeftOfCursor = 32,

    /// <summary>
    /// The ucBrl (Unicode Braille) bit is used by the functions lou_charToDots and lou_translate.
    /// It causes the dot patterns to be Unicode Braille rather than the liblouis representation.
    /// Note that you will not notice any change when setting ucBrl unless dotsIO is also set. lou_dotsToChar and lou_backTranslate recognize Unicode braille automatically. 
    /// </summary>
    UnicodeBraille = 64,

    /// <summary>
    /// Setting this bit disables the output of hexadecimal values when forward-translating undefined characters (characters that are not matched by any rule),
    /// and dot numbers when back-translating undefined braille patterns (braille patterns that are not matched by any rule).
    /// The default is for liblouis to output the hexadecimal value (as ’\xhhhh’) of an undefined character when forward-translating and the dot numbers (as \ddd/) of an undefined braille pattern when back-translating. 
    /// </summary>
    NoUndefined = 128,

    /// <summary>
    /// This flag specifies that back-translation input should be treated as an incomplete word. 
    /// Rules that apply only for complete words or at the end of a word will not take effect. 
    /// This is intended to be used when translating input typed on a braille keyboard to provide a rough idea to the user of the characters they are typing before the word is complete.
    /// </summary>
    PartialTranslation = 256
}
