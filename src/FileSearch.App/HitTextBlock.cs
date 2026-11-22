using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FileSearch.Core;

namespace FileSearch.App;

/// <summary>A TextBlock that renders a search hit with the matched ranges highlighted.</summary>
public class HitTextBlock : TextBlock
{
    public static readonly DependencyProperty HitProperty = DependencyProperty.Register(
        nameof(Hit), typeof(Hit), typeof(HitTextBlock), new PropertyMetadata(null, OnHitChanged));

    public Hit? Hit
    {
        get => (Hit?)GetValue(HitProperty);
        set => SetValue(HitProperty, value);
    }

    private static readonly Brush HighlightBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 235, 59)));

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static void OnHitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var tb = (HitTextBlock)d;
        tb.Inlines.Clear();
        if (e.NewValue is not Hit hit) return;

        var text = hit.Text;
        var trim = text.Length - text.TrimStart().Length;
        text = text[trim..];

        var pos = 0;
        foreach (var r in hit.Matches)
        {
            var start = Math.Max(r.Start - trim, 0);
            var end = Math.Min(r.Start + r.Length - trim, text.Length);
            if (end <= start || start < pos) continue;

            if (start > pos) tb.Inlines.Add(new Run(text[pos..start]));
            tb.Inlines.Add(new Run(text[start..end]) { Background = HighlightBrush, Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold });
            pos = end;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text[pos..]));
    }
}
