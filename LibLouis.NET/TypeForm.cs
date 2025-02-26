namespace LibLouis.NET;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Native Enum storage is uint16_t")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1069:Enums values should not be duplicated", Justification = "Multiple defintions in liblouis.h")]
public enum TypeForm : ushort
{
    PlainText = 0b_0000_0000,
    Italic = 0b_0000_0001,
    Emphasis = 0b_0000_0001,
    Underline = 0b_0000_0010,
    Bold = 0b_0000_0100,
    Emph4 = 0b_0000_1000,
    ForeignLanguage = 0b_0000_1000,
    Emph5 = 0b_0001_0000,
    Emph6 = 0b_0010_0000,
    Emph7 = 0b_0100_0000,
    Emph8 = 0b_1000_0000,
    Emph9 = 0b_0001_0000_0000,
    Emph10 = 0b_0010_0000_0000,
    Computer = 0b_0100_0000_0000,
    NoTranslation = 0b_1000_0000_0000,
    NoContract = 0b_0001_0000_0000_0000,
}
