using Avalonia;
using Avalonia.Controls;

namespace PingYi.App;

/// <summary>
/// Preserve each section's readable natural size, then share spare viewport height
/// between the capture, pipeline and status sections instead of leaving a blank tail.
/// Short windows keep the natural extent and scroll; no text is scaled or clipped.
/// </summary>
public sealed class WorkspaceBodyPanel : Panel
{
    public static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<WorkspaceBodyPanel, double>(nameof(ViewportHeight));
    private const double SectionSpacing = 12;

    public double ViewportHeight
    {
        get => GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    static WorkspaceBodyPanel() => AffectsMeasure<WorkspaceBodyPanel>(ViewportHeightProperty);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0, height = 0;
        var count = 0;
        foreach (var child in Children)
        {
            if (!child.IsVisible) continue;
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            count++;
        }
        height += Math.Max(0, count - 1) * SectionSpacing;
        // Viewport is the outer scroll area; the panel's margins must be subtracted
        // exactly once, otherwise even an empty page acquires a phantom scrollbar.
        var viewport = double.IsFinite(ViewportHeight)
            ? Math.Max(0, ViewportHeight - Margin.Top - Margin.Bottom) : 0;
        return new Size(width, Math.Max(height, viewport));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Children.Where(child => child.IsVisible).ToArray();
        if (children.Length == 0) return finalSize;
        var naturalHeight = children.Sum(child => child.DesiredSize.Height)
            + Math.Max(0, children.Length - 1) * SectionSpacing;
        var spare = Math.Max(0, finalSize.Height - naturalHeight);
        var status = children.FirstOrDefault(child => child.Name == "ModeStatusBorder");
        var hero = children.FirstOrDefault(child => child.Name == "CaptureWorkspaceCard");
        var pipeline = children.FirstOrDefault(child => child.Name == "PipelineWorkspaceCard");
        // Cap status growth so tall displays do not produce enormous mode badges.
        // The remaining room belongs inside the hero/pipeline, not below the status.
        var statusExtra = status is null ? 0 : Math.Min(72, spare * 0.55);
        var remaining = spare - statusExtra;
        var heroExtra = hero is null ? 0 : pipeline is null ? remaining : remaining * 0.8;
        var pipelineExtra = pipeline is null ? 0 : remaining - heroExtra;
        double y = 0;
        foreach (var child in children)
        {
            var height = child.DesiredSize.Height;
            if (ReferenceEquals(child, status)) height += statusExtra;
            if (ReferenceEquals(child, hero)) height += heroExtra;
            if (ReferenceEquals(child, pipeline)) height += pipelineExtra;
            child.Arrange(new Rect(0, y, finalSize.Width, height));
            y += height + SectionSpacing;
        }
        return finalSize;
    }
}
