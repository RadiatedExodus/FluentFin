using FluentFin.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Controls;

public sealed partial class MusicExpandedPlayer : UserControl
{
	public MusicPlaybackViewModel ViewModel { get; } = App.GetService<MusicPlaybackViewModel>();

	public MusicExpandedPlayer()
	{
		InitializeComponent();
		ProgressSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnProgressPointerReleased), true);
		VolumeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnVolumePointerReleased), true);
	}

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
