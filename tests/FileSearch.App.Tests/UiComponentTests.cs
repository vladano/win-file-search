using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using FileSearch.Core;

namespace FileSearch.App.Tests;

public class ConverterTests
{
    [Theory]
    [InlineData(true, "Resume")]
    [InlineData(false, "Pause")]
    public void PauseTextFollowsState(bool paused, string expected) =>
        Assert.Equal(expected, PauseTextConverter.Instance.Convert(paused, typeof(string), null!, CultureInfo.InvariantCulture));

    [Fact]
    public void PauseTextCannotConvertBack() =>
        Assert.Throws<NotSupportedException>(() => PauseTextConverter.Instance.ConvertBack("x", typeof(bool), null!, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("text", Visibility.Visible)]
    [InlineData("", Visibility.Collapsed)]
    [InlineData(null, Visibility.Collapsed)]
    public void NonEmptyStringsAreVisible(string? value, Visibility expected) =>
        Assert.Equal(expected, NonEmptyToVisibleConverter.Instance.Convert(value!, typeof(Visibility), null!, CultureInfo.InvariantCulture));

    [Fact]
    public void NonEmptyConverterCannotConvertBack() =>
        Assert.Throws<NotSupportedException>(() => NonEmptyToVisibleConverter.Instance.ConvertBack(Visibility.Visible, typeof(string), null!, CultureInfo.InvariantCulture));
}

public class HitTextBlockTests
{
    /// <summary>WPF elements need an STA thread.</summary>
    private static T OnSta<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new AggregateException(error);
        return result;
    }

    /// <summary>Returns the rendered runs as (text, highlighted) pairs.</summary>
    private static (string Text, bool Highlighted)[] Render(Hit? hit) => OnSta(() =>
    {
        var block = new HitTextBlock { Hit = hit };
        return block.Inlines.OfType<Run>().Select(r => (r.Text, r.Background is not null)).ToArray();
    });

    private static Hit H(string text, params (int Start, int Length)[] ranges) =>
        new(1, text, ranges.Select(r => new MatchRange(r.Start, r.Length)).ToArray());

    [Fact]
    public void HighlightsTheMatchedRange() =>
        Assert.Equal([("say ", false), ("ALIAS", true), (" now", false)], Render(H("say ALIAS now", (4, 5))));

    [Fact]
    public void HighlightsSeveralRanges() =>
        Assert.Equal([("a", true), ("-", false), ("a", true)], Render(H("a-a", (0, 1), (2, 1))));

    [Fact]
    public void NoMatchesGivesASinglePlainRun() =>
        Assert.Equal([("plain line", false)], Render(H("plain line")));

    [Fact]
    public void MatchAtTheStartAndEnd() =>
        Assert.Equal([("ab", true), ("cd", false), ("ef", true)], Render(H("abcdef", (0, 2), (4, 2))));

    [Fact]
    public void LeadingWhitespaceIsTrimmedAndRangesShifted() =>
        Assert.Equal([("call ", false), ("ALIAS", true)], Render(H("    call ALIAS", (9, 5))));

    [Fact]
    public void RangesInsideTheTrimmedIndentAreDropped() =>
        Assert.Equal([("text", false)], Render(H("   text", (0, 3))));

    [Fact]
    public void RangeStraddlingTheIndentIsClamped() =>
        Assert.Equal([("ab", true), (" rest", false)], Render(H("  ab rest", (1, 3))));

    [Fact]
    public void OverlappingOrOutOfOrderRangesAreSkippedNotCrashed() =>
        Assert.Equal([("abc", true), ("def", false)], Render(H("abcdef", (0, 3), (1, 2))));

    [Fact]
    public void RangesPastTheEndAreClamped() =>
        Assert.Equal([("ab", false), ("cd", true)], Render(H("abcd", (2, 50))));

    [Fact]
    public void NullHitClearsTheText()
    {
        var count = OnSta(() =>
        {
            var block = new HitTextBlock { Hit = H("something", (0, 1)) };
            block.Hit = null;
            return block.Inlines.Count;
        });
        Assert.Equal(0, count);
    }

    [Fact]
    public void ChangingTheHitReplacesThePreviousText()
    {
        var text = OnSta(() =>
        {
            var block = new HitTextBlock { Hit = H("first") };
            block.Hit = H("second");
            return string.Concat(block.Inlines.OfType<Run>().Select(r => r.Text));
        });
        Assert.Equal("second", text);
    }
}
