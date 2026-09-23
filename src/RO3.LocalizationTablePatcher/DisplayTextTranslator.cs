using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RO3.JapaneseMod
{
    // Display fallback for MeshUI and UI paths that bypass LanguageMain/Lua table hooks.
    internal sealed class DisplayTextTranslator
    {
        private readonly Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Regex Slot = new Regex(@"\$\{([0-9]+)\}");
        private Regex serverPattern;
        private string serverTranslation;
        private string questEnglish;
        private string questJapanese;
        private string serverPrefix;
        private string serverJapanesePrefix;

        public void Add(string id, string english, string japanese)
        {
            english = english.Replace(@"\n", "\n");
            japanese = japanese.Replace(@"\n", "\n");
            exact[english] = japanese;
            if (id == "13150600321")
            {
                questEnglish = english;
                questJapanese = japanese;
            }
            // This UI substitutes values before sending the text to its renderer.
            if (id == "35031")
            {
                english = StripOuterColor(english);
                serverTranslation = StripOuterColor(japanese);
                serverPrefix = english.Split(new[] { "${1}" }, StringSplitOptions.None)[0];
                serverJapanesePrefix = serverTranslation.Split(new[] { "${1}" }, StringSplitOptions.None)[0];
                string pattern = "\\A";
                int start = 0;
                foreach (Match slot in Slot.Matches(english))
                {
                    pattern += Regex.Escape(english.Substring(start, slot.Index - start));
                    pattern += "(?<p" + slot.Groups[1].Value + ">.+?)";
                    start = slot.Index + slot.Length;
                }
                pattern += Regex.Escape(english.Substring(start)) + "\\z";
                serverPattern = new Regex(pattern, RegexOptions.Singleline, TimeSpan.FromMilliseconds(20));
            }
        }

        private static string StripOuterColor(string text)
        {
            int close = text.IndexOf('>');
            if (text.StartsWith("<color=", StringComparison.Ordinal) && close >= 0 &&
                text.EndsWith("</color>", StringComparison.Ordinal))
                return text.Substring(close + 1, text.Length - close - 9);
            return text;
        }

        public string Translate(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            string translated;
            if (exact.TryGetValue(text, out translated)) return translated;
            string inner = StripOuterColor(text);
            if (inner != text)
            {
                translated = Translate(inner);
                if (translated != inner) return text.Substring(0, text.IndexOf('>') + 1) + translated + "</color>";
            }
            int end = text.Length;
            while (end > 0 && text[end - 1] == '!') end--;
            if (end != text.Length && exact.TryGetValue(text.Substring(0, end), out translated))
                return translated + text.Substring(end);
            if (serverPattern != null && text.StartsWith("Current Server Level Cap:", StringComparison.Ordinal))
            {
                try
                {
                    Match match = serverPattern.Match(text);
                    if (match.Success)
                        return Slot.Replace(serverTranslation, delegate(Match slot) { return match.Groups["p" + slot.Groups[1].Value].Value; });
                }
                catch (RegexMatchTimeoutException) { /* Leave unexpected text unchanged. */ }
            }
            // Quest tracker renders a combined rich-text block; XUnity can already
            // have translated its other lines. Replace only these known literals.
            if (!String.IsNullOrEmpty(questEnglish)) text = text.Replace(questEnglish, questJapanese);
            if (!String.IsNullOrEmpty(serverPrefix)) text = text.Replace(serverPrefix, serverJapanesePrefix);
            return text;
        }
    }
}
