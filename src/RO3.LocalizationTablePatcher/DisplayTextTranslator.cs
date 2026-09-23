using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RO3.JapaneseMod
{
    // Display fallback for MeshUI and UI paths that bypass LanguageMain/Lua table hooks.
    internal sealed class DisplayTextTranslator
    {
        private sealed class DynamicRule
        {
            public Regex Pattern;
            public string Japanese;
            public Dictionary<string, string> Groups;
        }

        private sealed class PrefixRule
        {
            public string English;
            public string Japanese;
        }

        private readonly Dictionary<string, string> exact = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Regex Slot = new Regex(@"\$\{([0-9]+)\}");
        private static readonly Regex RuntimeToken = new Regex(@"\$\{[0-9]+\}|@\{[0-9]+\}|\^\{[0-9]+\}");
        private static readonly HashSet<string> DynamicIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "57083",
            "11420000003",
            "11530400005",
            "11530400006",
            "11530500000",
            "11530500001",
            "11530500002",
            "11530500003",
            "11530500004",
            "12980100000",
            "10050100000",
            "12390100209",
        };
        private static readonly HashSet<string> NumericPrefixIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "10010300015",
            "10010300016",
            "10010400015",
            "10010400016",
            "10010400043",
        };
        private readonly List<DynamicRule> dynamicRules = new List<DynamicRule>();
        private readonly List<PrefixRule> numericPrefixRules = new List<PrefixRule>();
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
            if (DynamicIds.Contains(id))
            {
                DynamicRule rule = BuildDynamicRule(english, japanese);
                if (rule != null) dynamicRules.Add(rule);
            }
            if (NumericPrefixIds.Contains(id))
            {
                numericPrefixRules.Add(new PrefixRule { English = english, Japanese = japanese });
            }
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

        private static DynamicRule BuildDynamicRule(string english, string japanese)
        {
            MatchCollection tokens = RuntimeToken.Matches(english);
            if (tokens.Count == 0) return null;

            Dictionary<string, string> groups = new Dictionary<string, string>(StringComparer.Ordinal);
            string pattern = "\\A";
            int start = 0;
            int nextGroup = 0;
            foreach (Match tokenMatch in tokens)
            {
                pattern += Regex.Escape(english.Substring(start, tokenMatch.Index - start));
                string token = tokenMatch.Value;
                string group;
                if (groups.TryGetValue(token, out group))
                {
                    pattern += "\\k<" + group + ">";
                }
                else
                {
                    group = "d" + nextGroup++;
                    groups[token] = group;
                    if (token.StartsWith("^{", StringComparison.Ordinal))
                        pattern += "(?<" + group + ">(?:<[^>]+>)*)";
                    else
                        pattern += "(?<" + group + ">[\\s\\S]+?)";
                }
                start = tokenMatch.Index + tokenMatch.Length;
            }
            pattern += Regex.Escape(english.Substring(start)) + "\\z";
            return new DynamicRule
            {
                Pattern = new Regex(pattern, RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)),
                Japanese = japanese,
                Groups = groups,
            };
        }

        private static string ApplyDynamicRule(DynamicRule rule, Match match)
        {
            return RuntimeToken.Replace(rule.Japanese, delegate(Match token)
            {
                string group;
                if (!rule.Groups.TryGetValue(token.Value, out group)) return token.Value;
                return match.Groups[group].Value;
            });
        }

        private static bool LooksLikeNumericSuffix(string suffix)
        {
            int index = 0;
            while (index < suffix.Length && Char.IsWhiteSpace(suffix[index])) index++;
            if (index >= suffix.Length) return false;
            char current = suffix[index];
            return Char.IsDigit(current) || current == '+' || current == '-' || current == '−' || current == '.';
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
            foreach (DynamicRule rule in dynamicRules)
            {
                try
                {
                    Match match = rule.Pattern.Match(text);
                    if (match.Success) return ApplyDynamicRule(rule, match);
                }
                catch (RegexMatchTimeoutException) { /* Leave unexpected text unchanged. */ }
            }
            foreach (PrefixRule rule in numericPrefixRules)
            {
                if (!text.StartsWith(rule.English, StringComparison.Ordinal)) continue;
                string suffix = text.Substring(rule.English.Length);
                if (LooksLikeNumericSuffix(suffix)) return rule.Japanese + suffix;
            }
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
