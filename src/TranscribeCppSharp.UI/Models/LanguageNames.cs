namespace TranscribeCppSharp.UI.Models;

using System.Collections.Generic;

/// <summary>
/// An English name for the language codes a model reports, for display beside
/// the code.
/// </summary>
/// <remarks>
/// The names come from this table, not from the native library: the list of
/// languages a picker offers is still whatever the loaded model declares, and a
/// code this table does not know is shown as it came back rather than guessed.
/// That is why the table covers the codes the Whisper family uses and no more —
/// it is a reading aid, not a claim about what any other family accepts.
/// </remarks>
public static class LanguageNames
{
    /// <summary>
    /// The text a picker shows for one code: the code, then its name. A code
    /// outside the table is returned unchanged.
    /// </summary>
    /// <param name="code">The language code the model reported, or <c>auto</c>.</param>
    /// <returns>Display text, never null.</returns>
    public static string Describe(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        if (string.Equals(code, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return $"{code} — detect the language";
        }

        return Names.TryGetValue(code, out string? name) ? $"{code} — {name}" : code;
    }

    /// <summary>
    /// The codes Whisper uses, with the English name each one stands for.
    /// </summary>
    /// <remarks>
    /// Case-insensitive because a model is free to report "EN"; the display keeps
    /// the code as it was passed. ISO 639-1 names are the canonical English ones.
    /// </remarks>
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["af"] = "Afrikaans",
        ["am"] = "Amharic",
        ["ar"] = "Arabic",
        ["as"] = "Assamese",
        ["az"] = "Azerbaijani",
        ["ba"] = "Bashkir",
        ["be"] = "Belarusian",
        ["bg"] = "Bulgarian",
        ["bn"] = "Bengali",
        ["bo"] = "Tibetan",
        ["br"] = "Breton",
        ["bs"] = "Bosnian",
        ["ca"] = "Catalan",
        ["cs"] = "Czech",
        ["cy"] = "Welsh",
        ["da"] = "Danish",
        ["de"] = "German",
        ["el"] = "Greek",
        ["en"] = "English",
        ["es"] = "Spanish",
        ["et"] = "Estonian",
        ["eu"] = "Basque",
        ["fa"] = "Persian",
        ["fi"] = "Finnish",
        ["fo"] = "Faroese",
        ["fr"] = "French",
        ["gl"] = "Galician",
        ["gu"] = "Gujarati",
        ["ha"] = "Hausa",
        ["haw"] = "Hawaiian",
        ["he"] = "Hebrew",
        ["hi"] = "Hindi",
        ["hr"] = "Croatian",
        ["ht"] = "Haitian Creole",
        ["hu"] = "Hungarian",
        ["hy"] = "Armenian",
        ["id"] = "Indonesian",
        ["is"] = "Icelandic",
        ["it"] = "Italian",
        ["ja"] = "Japanese",
        ["jw"] = "Javanese",
        ["jv"] = "Javanese",
        ["ka"] = "Georgian",
        ["kk"] = "Kazakh",
        ["km"] = "Khmer",
        ["kn"] = "Kannada",
        ["ko"] = "Korean",
        ["la"] = "Latin",
        ["lb"] = "Luxembourgish",
        ["ln"] = "Lingala",
        ["lt"] = "Lithuanian",
        ["lv"] = "Latvian",
        ["mg"] = "Malagasy",
        ["mi"] = "Maori",
        ["mk"] = "Macedonian",
        ["ml"] = "Malayalam",
        ["mn"] = "Mongolian",
        ["mr"] = "Marathi",
        ["ms"] = "Malay",
        ["mt"] = "Maltese",
        ["my"] = "Myanmar",
        ["ne"] = "Nepali",
        ["nl"] = "Dutch",
        ["nn"] = "Nynorsk",
        ["no"] = "Norwegian",
        ["oc"] = "Occitan",
        ["pa"] = "Punjabi",
        ["pl"] = "Polish",
        ["ps"] = "Pashto",
        ["pt"] = "Portuguese",
        ["ro"] = "Romanian",
        ["ru"] = "Russian",
        ["sa"] = "Sanskrit",
        ["sd"] = "Sindhi",
        ["si"] = "Sinhala",
        ["sk"] = "Slovak",
        ["sl"] = "Slovenian",
        ["sn"] = "Shona",
        ["so"] = "Somali",
        ["sq"] = "Albanian",
        ["sr"] = "Serbian",
        ["su"] = "Sundanese",
        ["sv"] = "Swedish",
        ["sw"] = "Swahili",
        ["ta"] = "Tamil",
        ["te"] = "Telugu",
        ["tg"] = "Tajik",
        ["th"] = "Thai",
        ["tk"] = "Turkmen",
        ["tl"] = "Tagalog",
        ["tr"] = "Turkish",
        ["tt"] = "Tatar",
        ["uk"] = "Ukrainian",
        ["ur"] = "Urdu",
        ["uz"] = "Uzbek",
        ["vi"] = "Vietnamese",
        ["yi"] = "Yiddish",
        ["yo"] = "Yoruba",
        ["zh"] = "Chinese",
    };
}
