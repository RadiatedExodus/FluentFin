using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace FluentFin.ViewModels;

public partial class LyricsLineViewModel(string text, TimeSpan? start, bool isTimed) : ObservableObject
{
	public string Text { get; } = text;
	public TimeSpan? Start { get; } = start;
	public bool IsTimed { get; } = isTimed;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(FontSize))]
	[NotifyPropertyChangedFor(nameof(Opacity))]
	[NotifyPropertyChangedFor(nameof(Foreground))]
	public partial bool IsActive { get; set; }

	public double FontSize => !IsTimed ? 24 : IsActive ? 30 : 22;
	public double Opacity => !IsTimed ? 0.88 : IsActive ? 1 : 0.58;
	public Brush Foreground => !IsTimed || IsActive
		? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255))
		: new SolidColorBrush(Windows.UI.Color.FromArgb(180, 255, 255, 255));
}
