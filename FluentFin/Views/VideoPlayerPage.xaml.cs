using FluentFin.ViewModels;
using FluentFin.Playback.Presentation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentFin.Views;

public sealed partial class VideoPlayerPage : Page
{
	public VideoPlayerViewModel ViewModel { get; } = App.GetService<VideoPlayerViewModel>();

	public VideoPlayerPage()
	{
		InitializeComponent();
	}

	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		ViewModel.ToggleFullScreen = () => MediaPlayerHost.OnPlayerDoubleTapped(MediaPlayerHost, new Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs());
	}

	public Task ActivateAsync(object? parameter)
	{
		ViewModel.ToggleFullScreen = () => MediaPlayerHost.OnPlayerDoubleTapped(MediaPlayerHost, new Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs());
		return ViewModel.OnNavigatedTo(parameter!);
	}

	public Task DeactivateAsync()
	{
		return ViewModel.OnNavigatedFrom();
	}

	private async void BackButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
	{
		await App.GetService<IPlaybackPresentationManager>().HideAsync();
	}
}
