using FluentFin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FluentFin.Controls;

public sealed partial class MusicMiniPlayer : UserControl
{
	public MusicPlaybackViewModel ViewModel { get; } = App.GetService<MusicPlaybackViewModel>();

	public MusicMiniPlayer()
	{
		InitializeComponent();
		ProgressSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnProgressPointerReleased), true);
		VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumePointerReleased), true);
	}

	private void Root_Tapped(object sender, TappedRoutedEventArgs e)
	{
		if (IsInteractiveSource(e.OriginalSource as DependencyObject))
		{
			return;
		}

		ViewModel.OpenExpandedCommand.Execute(null);
		e.Handled = true;
	}

	private void AlbumLink_Tapped(object sender, TappedRoutedEventArgs e)
	{
		ViewModel.OpenAlbumCommand.Execute(null);
		e.Handled = true;
	}

	private static bool IsInteractiveSource(DependencyObject? source)
	{
		while (source is not null)
		{
			if (source is ButtonBase or Slider or NumberBox)
			{
				return true;
			}

			source = VisualTreeHelper.GetParent(source);
		}

		return false;
	}

	private void Interactive_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
	private void Interactive_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

	private async void OnProgressPointerReleased(object sender, PointerRoutedEventArgs e)
	{
		e.Handled = true;
		await ViewModel.SeekToSeconds(ProgressSlider.Value);
	}

	private async void OnVolumePointerReleased(object sender, PointerRoutedEventArgs e)
	{
		e.Handled = true;
		await ViewModel.SetVolumeFromPercent((int)Math.Round(VolumeSlider.Value));
	}

	private async void VolumeNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (double.IsNaN(args.NewValue))
		{
			return;
		}

		await ViewModel.SetVolumeFromPercent((int)Math.Round(args.NewValue));
	}
}
