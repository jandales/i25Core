using System.Text.RegularExpressions;

namespace i25Core.Infrastructure.Localization
{
    /// <summary>
    /// Picks the right form of a plural message (Laravel's trans_choice).
    /// Forms are separated by "|". A form can start with a count condition:
    /// <c>{0}</c> exactly 0, <c>[1,19]</c> from 1 to 19, <c>[20,*]</c> 20 or more, <c>[*,-1]</c> -1 or less.
    /// Forms without a condition are matched by position: the first is the singular (count 1), the second is the plural.
    /// </summary>
    internal static class PluralSelector
    {
        private static readonly Regex Condition = new(
            @"^\s*(?:\{(?<exact>-?\d+)\}|\[(?<from>-?\d+|\*)\s*,\s*(?<to>-?\d+|\*)\])\s*(?<body>.*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public static string Select(string message, int count)
        {
            var forms = message.Split('|');
            var plain = new List<string>();

            foreach (var form in forms)
            {
                var match = Condition.Match(form);

                if (!match.Success)
                {
                    plain.Add(form.Trim());
                    continue;
                }

                if (Matches(match, count))
                    return match.Groups["body"].Value.Trim();
            }

            // No explicit condition matched: choose by position, like English (1 = singular, anything else = plural).
            if (plain.Count == 0)
                return forms[0].Trim();

            var index = count == 1 ? 0 : 1;

            return plain.Count == 1 || index >= plain.Count ? plain[0] : plain[index];
        }

        private static bool Matches(Match match, int count)
        {
            if (match.Groups["exact"].Success)
                return count == int.Parse(match.Groups["exact"].Value);

            var from = match.Groups["from"].Value;
            var to = match.Groups["to"].Value;

            var aboveStart = from == "*" || count >= int.Parse(from);
            var belowEnd = to == "*" || count <= int.Parse(to);

            return aboveStart && belowEnd;
        }
    }
}
