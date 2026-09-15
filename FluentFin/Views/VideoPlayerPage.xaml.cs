using FluentFin.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
		ViewModel.ToggleFullScreen = ToggleFullscreen;
	}

	public Task ActivateAsync(object? parameter)
	{
		ViewModel.ToggleFullScreen = ToggleFullscreen;
		return ViewModel.OnNavigatedTo(parameter!);
	}

	public Task DeactivateAsync()
	{
		return ViewModel.OnNavigatedFrom();
	}

	private void Root_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
	{
		ToggleFullscreen();
	}

	private void ToggleFullscreen()
	{
		var current = App.MainWindow.AppWindow.Presenter.Kind;
		var presenterKind = current == AppWindowPresenterKind.Overlapped
			? AppWindowPresenterKind.FullScreen
			: AppWindowPresenterKind.Overlapped;

		TransportControls.FullWindowSymbol.Symbol = presenterKind == AppWindowPresenterKind.FullScreen ? Symbol.BackToWindow : Symbol.FullScreen;
		App.MainWindow.AppWindow.SetPresenter(presenterKind);
	}
}
