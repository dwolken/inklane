using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Inklane.Controls;

internal static class Studio
{
    // Each silhouette is drawn on the same 24-unit grid.
    private static readonly Dictionary<string, string> Paths = new()
    {
        ["Shape"] = "M3,4 H15 V16 H3 Z M13,12 A5,5 0 1 0 13.01,12",
        ["Pen"] = "M4,20 L5.6,13.8 L16.5,2.9 Q17.6,1.8 18.8,3 L21,5.2 Q22.2,6.4 21,7.6 L10.1,18.5 Z M14.2,5.2 L18.9,9.8 M5.6,13.8 L10.1,18.5",
        ["Select"] = "M5,3 L19,13 L12.6,14.1 L10,21 Z",
        ["Eraser"] = "M3.7,13.7 L13.5,3.9 Q14.7,2.7 15.9,3.9 L21,9 Q22,10 21,11 L12,20 L9.3,20 Z M8,9.4 L16,17.4 M11.5,20 L21,20",
        ["Text"] = "M3,7 V4 H21 V7 M12,4 V20 M8,20 H16",
        ["Note"] = "M4,4 H20 V15 L14,21 H4 Z M14,21 V15 H20 M8,8 H16 M8,11 H13",
        ["Pan"] = "M8,12 V5.6 Q8,3.3 10,3.4 Q12,3.4 12,5.6 V10 M12,5.6 V4.5 Q12,2.6 14,2.7 Q16,2.7 16,5 V11 M16,6.7 Q16,4.7 18,5 Q20,5.1 20,7.2 V14 Q20,21 14,21 H11 Q8,21 6,18 L2.8,13.1 Q2,11 4,10.3 Q5,10 6.1,11.4 L8,13.8",
        ["Open"] = "M3,18 V6 Q3,4 5,4 H10 L12.5,7 H20 V10 M3,19 L6,10 H22 L19,19 Z",
        ["Image"] = "M3,4 H21 V20 H3 Z M3,16 L8,11 L12,15 L15,12 L21,17 M16,7.5 A1.4,1.4 0 1 0 16.01,7.5",
        ["Notebook"] = "M6,3 H20 V21 H6 Q4,21 4,19 V5 Q4,3 6,3 Z M8,3 V21 M12,8 H17",
        ["Save"] = "M4,3 H17 L21,7 V21 H3 V4 Z M7,3 V9 H16 V3 M7,21 V14 H17 V21",
        ["Undo"] = "M8,5 L3,10 L8,15 M3,10 H14 C22,10 22,20 15,20",
        ["Redo"] = "M16,5 L21,10 L16,15 M21,10 H10 C2,10 2,20 9,20",
        ["Notes"] = "M7,4 H21 V16 H16 L11,20 V16 H7 Z M3,8 V20 H7 M10,8 H18 M10,11 H16",
        ["More"] = "M4,12 H5 M11.5,12 H12.5 M19,12 H20",
        ["Previous"] = "M14.5,6 L8.5,12 L14.5,18",
        ["Next"] = "M9.5,6 L15.5,12 L9.5,18",
        ["Plus"] = "M12,5 V19 M5,12 H19",
        ["Minus"] = "M5,12 H19",
        ["Down"] = "M6,9 L12,15 L18,9",
        ["Close"] = "M6,6 L18,18 M18,6 L6,18",
        ["Arrow"] = "M4,12 H20 M14,6 L20,12 L14,18",
        ["Search"] = "M10.5,3.5 A7,7 0 1 0 10.51,3.5 M16,16 L21,21",
        ["Paper"] = "M5,2 H15 L20,7 V22 H5 Z M15,2 V7 H20 M8,11 H17 M8,15 H17",
        ["Zoom"] = "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15",
        ["Home"] = "M3,11 L12,3 L21,11 M5,10 V21 H19 V10",
    };

    public static FrameworkElement Icon(string key, double size = 18)
    {
        var path = new Path
        {
            Data = Geometry.Parse(Paths[key]), StrokeThickness = key == "More" ? 3.5 : 1.65,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            SnapsToDevicePixels = false
        };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground")
        { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    public static TextBlock Label(string text, double size = 13, string brush = "TextBrush", bool strong = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
            FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        label.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return label;
    }

    public static StackPanel Line(params UIElement[] elements)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var element in elements) row.Children.Add(element);
        return row;
    }

    public static Button Action(string key, string icon, Action action, bool label = false, string style = "QuietButton")
    {
        var button = new Button { ToolTip = L.T(key), Height = 34, Margin = new Thickness(1,0,1,0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, style);
        AutomationProperties.SetName(button, L.T(key));
        UIElement content = Icon(icon);
        if (label)
        {
            var caption = new TextBlock { Text = L.T(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7,0,0,0) };
            content = Line(content, caption);
        }
        button.Content = content;
        button.Click += (_, _) => action();
        return button;
    }

    public static Border Rule(bool vertical = false)
    {
        var line = new Border { IsHitTestVisible = false };
        line.SetResourceReference(Border.BackgroundProperty, "BorderBrush");
        if (vertical) { line.Width = 1; line.Height = 21; line.Margin = new Thickness(8,0,8,0); }
        else line.Height = 1;
        return line;
    }
}
