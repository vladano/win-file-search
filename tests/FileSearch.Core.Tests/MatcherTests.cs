using FileSearch.Core;

namespace FileSearch.Core.Tests;

public class MatcherTests
{
    [Theory]
    [InlineData("*.prg", "a.PRG", true)]
    [InlineData("*.prg", "a.txt", false)]
    [InlineData("*.cs;*.xaml", "Main.xaml", true)]
    [InlineData("*.cs,*.xaml", "Main.xaml", true)]
    [InlineData("*.*", "noext", true)]
    [InlineData("", "anything", true)]
    [InlineData("file?.txt", "file1.txt", true)]
    [InlineData("file?.txt", "file12.txt", false)]
    public void DosNames(string pattern, string name, bool expected) =>
        Assert.Equal(expected, new NameMatcher(pattern, NameMode.Dos, false).IsMatch(name));

    [Fact]
    public void DosNamesRespectMatchCase()
    {
        Assert.False(new NameMatcher("*.PRG", NameMode.Dos, true).IsMatch("a.prg"));
        Assert.True(new NameMatcher("*.PRG", NameMode.Dos, true).IsMatch("a.PRG"));
    }

    [Fact]
    public void RegexNames() =>
        Assert.True(new NameMatcher(@"^\d+\.log$", NameMode.Regex, false).IsMatch("123.log"));

    [Theory]
    [InlineData("hello AND world", "hello big world", true)]
    [InlineData("hello AND world", "hello only", false)]
    [InlineData("hello world", "world and hello", true)] // implicit AND
    [InlineData("hello OR world", "just world", true)]
    [InlineData("hello NOT world", "hello there", true)]
    [InlineData("hello NOT world", "hello world", false)]
    [InlineData("\"big world\"", "a big world", true)]
    [InlineData("\"big world\"", "big small world", false)]
    [InlineData("(a OR b) AND c", "b c", true)]
    [InlineData("(a OR b) AND c", "a b", false)]
    [InlineData("NOT secret", "public", true)]
    public void Boolean(string expr, string content, bool expected) =>
        Assert.Equal(expected, ContentMatcher.Create(expr, ContentMode.Boolean, false).IsMatch(content));

    [Fact]
    public void PlainTextIsLiteral()
    {
        var m = ContentMatcher.Create("a.b(", ContentMode.Plain, false);
        Assert.True(m.IsMatch("xx A.B( yy"));
        Assert.False(m.IsMatch("axb("));
    }

    [Fact]
    public void MatchCase()
    {
        Assert.False(ContentMatcher.Create("ALIAS", ContentMode.Plain, true).IsMatch("alias"));
        Assert.True(ContentMatcher.Create("ALIAS", ContentMode.Plain, false).IsMatch("alias"));
    }

    [Fact]
    public void RegexLineAnchors() =>
        Assert.True(ContentMatcher.Create("^bar$", ContentMode.Regex, false).IsMatch("foo\nbar\nbaz"));

    [Theory]
    [InlineData("a AND (b")]
    [InlineData("\"unterminated")]
    [InlineData("AND")]
    public void InvalidBooleanThrows(string expr) =>
        Assert.Throws<FormatException>(() => ContentMatcher.Create(expr, ContentMode.Boolean, false));

    [Fact]
    public void FindInLineMergesOverlaps()
    {
        var m = ContentMatcher.Create("ab OR bc", ContentMode.Boolean, false);
        var ranges = new List<MatchRange>();
        m.FindInLine("xabcx", ranges);
        Assert.Equal([new MatchRange(1, 3)], ranges);
    }
}

public class MatcherEdgeCaseTests
{
    [Fact]
    public void AndBindsTighterThanOr()
    {
        var m = ContentMatcher.Create("a OR b AND c", ContentMode.Boolean, false);
        Assert.True(m.IsMatch("a"));        // a OR (b AND c)
        Assert.True(m.IsMatch("b c"));
        Assert.False(m.IsMatch("b"));
    }

    [Fact]
    public void LowercaseOperatorsAreOrdinaryWords()
    {
        var m = ContentMatcher.Create("this and that", ContentMode.Boolean, false);
        Assert.True(m.IsMatch("that and this"));  // implicit AND of three words
        Assert.False(m.IsMatch("this that"));     // "and" must be present
    }

    [Fact]
    public void NegatedTermsAreNotHighlighted()
    {
        var m = ContentMatcher.Create("keep NOT drop", ContentMode.Boolean, false);
        Assert.True(m.LineHasMatch("keep this"));
        Assert.False(m.LineHasMatch("drop this"));
        var ranges = new List<MatchRange>();
        m.FindInLine("keep drop", ranges);
        Assert.Equal([new MatchRange(0, 4)], ranges);
    }

    [Fact]
    public void FindInLineClearsPreviousResults()
    {
        var m = ContentMatcher.Create("x", ContentMode.Plain, false);
        var ranges = new List<MatchRange> { new(99, 1) };
        m.FindInLine("no match here", ranges);
        Assert.Empty(ranges);
    }

    [Fact]
    public void FindInLineReturnsEveryOccurrence()
    {
        var m = ContentMatcher.Create("ab", ContentMode.Plain, false);
        var ranges = new List<MatchRange>();
        m.FindInLine("ab-AB-ab", ranges);
        Assert.Equal([new MatchRange(0, 2), new MatchRange(3, 2), new MatchRange(6, 2)], ranges);
    }

    [Fact]
    public void EmptyRegexMatchesAreNotHighlighted()
    {
        var m = ContentMatcher.Create("x*", ContentMode.Regex, false);
        var ranges = new List<MatchRange>();
        m.FindInLine("abc", ranges);
        Assert.Empty(ranges);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void EmptyContentTextIsRejected(string? text) =>
        Assert.Throws<ArgumentException>(() => ContentMatcher.Create(text!, ContentMode.Plain, false));

    [Fact]
    public void InvalidRegexIsRejected() =>
        Assert.ThrowsAny<ArgumentException>(() => ContentMatcher.Create("(", ContentMode.Regex, false));

    [Theory]
    [InlineData("a OR")]
    [InlineData("NOT")]
    [InlineData("()")]
    [InlineData("a ) b")]
    [InlineData("\"\"")]
    public void MoreInvalidBooleanExpressions(string expr) =>
        Assert.Throws<FormatException>(() => ContentMatcher.Create(expr, ContentMode.Boolean, false));

    [Fact]
    public void NameMatcherInvalidRegexIsRejected() =>
        Assert.ThrowsAny<ArgumentException>(() => new NameMatcher("[", NameMode.Regex, false));

    [Fact]
    public void NameMatcherRegexMatchCase()
    {
        Assert.True(new NameMatcher("^A", NameMode.Regex, false).IsMatch("abc"));
        Assert.False(new NameMatcher("^A", NameMode.Regex, true).IsMatch("abc"));
    }

    [Fact]
    public void NameMatcherEmptyRegexMatchesEverything() =>
        Assert.True(new NameMatcher("", NameMode.Regex, false).IsMatch("anything"));

    [Fact]
    public void NameMatcherWildcardsDoNotMatchPartialNames()
    {
        var m = new NameMatcher("abc", NameMode.Dos, false);
        Assert.True(m.IsMatch("ABC"));
        Assert.False(m.IsMatch("abcd"));
    }
}
