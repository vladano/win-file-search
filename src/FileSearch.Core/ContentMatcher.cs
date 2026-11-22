using System.Text;
using System.Text.RegularExpressions;

namespace FileSearch.Core;

public readonly record struct MatchRange(int Start, int Length);

/// <summary>
/// Matches file content using plain text, a regular expression, or a boolean expression
/// (AND, OR, NOT in upper case, parentheses, "quoted phrases"; adjacent terms mean AND).
/// </summary>
public sealed class ContentMatcher
{
    private readonly Node _root;
    private readonly List<Regex> _positive = [];

    private ContentMatcher(Node root)
    {
        _root = root;
        root.CollectPositive(_positive);
    }

    public static ContentMatcher Create(string text, ContentMode mode, bool matchCase)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("Search text is empty.", nameof(text));

        var opts = RegexOptions.CultureInvariant | RegexOptions.Compiled
                   | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        var timeout = SearchTuning.ContentRegexTimeout;

        Regex Literal(string s) => new(Regex.Escape(s), opts, timeout);

        switch (mode)
        {
            case ContentMode.Plain:
                return new ContentMatcher(new Term(Literal(text)));
            case ContentMode.Regex:
                return new ContentMatcher(new Term(new Regex(text, opts | RegexOptions.Multiline, timeout)));
            default:
                return new ContentMatcher(new BooleanParser(Tokenize(text), Literal).Parse());
        }
    }

    /// <summary>True when the whole file content satisfies the expression.</summary>
    public bool IsMatch(string content) => _root.Eval(r => r.IsMatch(content));

    /// <summary>Finds highlighted ranges of positive terms within a single line.</summary>
    public void FindInLine(string line, List<MatchRange> into)
    {
        into.Clear();
        foreach (var rx in _positive)
            foreach (var m in rx.EnumerateMatches(line))
                if (m.Length > 0) into.Add(new MatchRange(m.Index, m.Length));

        if (into.Count < 2) return;
        into.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<MatchRange>(into.Count) { into[0] };
        for (var i = 1; i < into.Count; i++)
        {
            var last = merged[^1];
            var cur = into[i];
            if (cur.Start <= last.Start + last.Length)
                merged[^1] = new MatchRange(last.Start, Math.Max(last.Length, cur.Start + cur.Length - last.Start));
            else
                merged.Add(cur);
        }
        into.Clear();
        into.AddRange(merged);
    }

    /// <summary>True when the line contains at least one positive term.</summary>
    public bool LineHasMatch(string line)
    {
        foreach (var rx in _positive)
            if (rx.IsMatch(line)) return true;
        return false;
    }

    // ---- expression tree ----

    private abstract class Node
    {
        public abstract bool Eval(Func<Regex, bool> test);
        public abstract void CollectPositive(List<Regex> into);
    }

    private sealed class Term(Regex regex) : Node
    {
        public override bool Eval(Func<Regex, bool> test) => test(regex);
        public override void CollectPositive(List<Regex> into) => into.Add(regex);
    }

    private sealed class Not(Node inner) : Node
    {
        public override bool Eval(Func<Regex, bool> test) => !inner.Eval(test);
        public override void CollectPositive(List<Regex> into) { /* negated terms are not highlighted */ }
    }

    private sealed class And(Node left, Node right) : Node
    {
        public override bool Eval(Func<Regex, bool> test) => left.Eval(test) && right.Eval(test);
        public override void CollectPositive(List<Regex> into) { left.CollectPositive(into); right.CollectPositive(into); }
    }

    private sealed class Or(Node left, Node right) : Node
    {
        public override bool Eval(Func<Regex, bool> test) => left.Eval(test) || right.Eval(test);
        public override void CollectPositive(List<Regex> into) { left.CollectPositive(into); right.CollectPositive(into); }
    }

    // ---- boolean parsing ----

    private enum TokenKind { Word, Phrase, And, Or, Not, Open, Close }

    private readonly record struct Token(TokenKind Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenKind.Open, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.Close, ")")); i++; continue; }
            if (c == '"')
            {
                var end = s.IndexOf('"', i + 1);
                if (end < 0) throw new FormatException("Missing closing quote in search expression.");
                tokens.Add(new Token(TokenKind.Phrase, s[(i + 1)..end]));
                i = end + 1;
                continue;
            }

            var sb = new StringBuilder();
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')' && s[i] != '"')
                sb.Append(s[i++]);
            var word = sb.ToString();
            tokens.Add(word switch
            {
                "AND" => new Token(TokenKind.And, word),
                "OR" => new Token(TokenKind.Or, word),
                "NOT" => new Token(TokenKind.Not, word),
                _ => new Token(TokenKind.Word, word),
            });
        }
        return tokens;
    }

    private sealed class BooleanParser(List<Token> tokens, Func<string, Regex> literal)
    {
        private int _pos;

        public Node Parse()
        {
            if (tokens.Count == 0) throw new FormatException("Search expression is empty.");
            var node = ParseOr();
            if (_pos < tokens.Count) throw new FormatException($"Unexpected '{tokens[_pos].Text}' in search expression.");
            return node;
        }

        private Node ParseOr()
        {
            var left = ParseAnd();
            while (_pos < tokens.Count && tokens[_pos].Kind == TokenKind.Or)
            {
                _pos++;
                left = new Or(left, ParseAnd());
            }
            return left;
        }

        private Node ParseAnd()
        {
            var left = ParseNot();
            while (_pos < tokens.Count)
            {
                var k = tokens[_pos].Kind;
                if (k == TokenKind.And) { _pos++; left = new And(left, ParseNot()); }
                else if (k is TokenKind.Word or TokenKind.Phrase or TokenKind.Not or TokenKind.Open)
                    left = new And(left, ParseNot());
                else break;
            }
            return left;
        }

        private Node ParseNot()
        {
            if (_pos < tokens.Count && tokens[_pos].Kind == TokenKind.Not)
            {
                _pos++;
                return new Not(ParseNot());
            }
            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            if (_pos >= tokens.Count) throw new FormatException("Unexpected end of search expression.");
            var t = tokens[_pos++];
            switch (t.Kind)
            {
                case TokenKind.Word:
                case TokenKind.Phrase:
                    if (t.Text.Length == 0) throw new FormatException("Empty phrase in search expression.");
                    return new Term(literal(t.Text));
                case TokenKind.Open:
                    var inner = ParseOr();
                    if (_pos >= tokens.Count || tokens[_pos].Kind != TokenKind.Close)
                        throw new FormatException("Missing ')' in search expression.");
                    _pos++;
                    return inner;
                default:
                    throw new FormatException($"Unexpected '{t.Text}' in search expression.");
            }
        }
    }
}
