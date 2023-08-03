using Avalonia.Media;

namespace K8sFileBrowser.Models;

public class Message
{
    public bool IsVisible { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public IBrush Color => IsError ? new SolidColorBrush(Avalonia.Media.Color.FromRgb(74, 7, 2)) : Brushes.Black;
    public double Opacity => 0.7; 
}
