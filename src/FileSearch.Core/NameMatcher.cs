using System.IO.Enumeration;
using System.Text.RegularExpressions;

namespace FileSearch.Core;

/// <summary>Matches file names against DOS wildcard lists or a regular expression.</summary>
public sealed class NameMatcher
{
    private readonly string[] _patterns = [];
    private readonly Regex? _regex;
    private readonly bool _ignoreCase;

    public NameMatcher(string? pattern, NameMode mode, bool matchCase)
    {
        _ignoreCase = !matchCase;
        if (mode == NameMode.Regex)
        {
            var opts = RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
            _regex = new Regex(string.IsNullOrEmpty(pattern) ? ".*" : pattern, opts, SearchTuning.NameRegexTimeout);
            return;
        }

        var list = SearchCriteria.SplitList(pattern, allowComma: true)
            .Select(p => p == "*.*" ? "*" : p)
            .ToArray();
        _patterns = list.Length == 0 ? ["*"] : list;
    }

    public bool IsMatch(ReadOnlySpan<char> name)
    {
        if (_regex is not null)
        {
            try { return _regex.IsMatch(name); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        foreach (var p in _patterns)
            if (FileSystemName.MatchesSimpleExpression(p, name, _ignoreCase))
                return true;
        return false;
    }
}
