using Avalonia.Controls;
using Avalonia.Media;

namespace ShiroBot.CardStudio.Samples;

/// <summary>供 DLL 反射加载功能试用的最小自定义控件。</summary>
public sealed class SampleBadgeControl : UserControl
{
    public SampleBadgeControl()
    {
        Content = new Border
        {
            Background = Brush.Parse("#6758E8"),
            CornerRadius = new Avalonia.CornerRadius(16),
            Padding = new Avalonia.Thickness(24),
            Child = new TextBlock
            {
                Text = "ShiroBot · Reflected Control",
                Foreground = Brushes.White,
                FontSize = 26,
                FontWeight = FontWeight.SemiBold
            }
        };
    }
}
