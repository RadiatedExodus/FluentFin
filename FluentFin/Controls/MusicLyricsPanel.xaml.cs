using System.ComponentModel;
using FluentFin.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Controls;

public sealed partial class MusicLyricsPanel : UserControl
{
	private DateTimeOffset _suppressAutoScrollUntil = DateTimeOffset.MinValue;

	public MusicLyricsViewModel ViewModel { get; } = App.GetService<MusicLyricsViewModel>();

	public MusicLyricsPanel()
	{
		InitializeComponent();
		ViewModel.PropertyChanged += ViewModel_PropertyChanged;
		LyricsList.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(LyricsList_PointerWheelChanged), true);
	}

	private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(MusicLyricsViewModel.ActiveLineIndex))
		{
			ScrollActiveLineIntoView();
		}
		else if (e.PropertyName is nameof(MusicLyricsViewModel.CurrentItem))
		{
			_suppressAutoScrollUntil = DateTimeOffset.MinValue;
		}
	}

	private void ScrollActiveLineIntoView()
	{
		if (DateTimeOffset.Now < _suppressAutoScrollUntil)
		{
			return;
		}

		var index = ViewModel.ActiveLineIndex;
		if (index < 0 || index >= ViewModel.Lines.Count)
		{
			return;
		}

		LyricsList.ScrollIntoView(ViewModel.Lines[index], ScrollIntoViewAlignment.Leading);
	}

	private void LyricsList_PointerPressed(object sender, PointerRoutedEventArgs e)
	{
		_suppressAutoScrollUntil = DateTimeOffset.Now.AddSeconds(4);
	}

	private void LyricsList_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
	{
		_suppressAutoScrollUntil = DateTimeOffset.Now.AddSeconds(4);
	}
}
