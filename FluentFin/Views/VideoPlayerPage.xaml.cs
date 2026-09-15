using FluentFin.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace FluentFin.Views;

public sealed partial class VideoPlayerPage : Page
{
	private readonly DispatcherTimer _hideChromeTimer = new() { Interval = TimeSpan.FromSeconds(3) };

	public VideoPlayerViewModel ViewModel { get; } = App.GetService<VideoPlayerViewModel>();

	public VideoPlayerPage()
	{
		InitializeComponent();
		_hideChromeTimer.Tick += HideChromeTimer_Tick;
	}

	protected override void OnNavigatedTo(NavigationEventArgs e)
	{
		ViewModel.ToggleFullScreen = ToggleFullscreen;
	}

	public Task ActivateAsync(object? parameter)
	{
		ViewModel.ToggleFullScreen = ToggleFullscreen;
		ShowPlaybackChrome();
		return ViewModel.OnNavigatedTo(parameter!);
	}

	public Task DeactivateAsync()
	{
		_hideChromeTimer.Stop();
		RestoreWindowedPresenter();
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

	private void RestoreWindowedPresenter()
	{
		if (App.MainWindow.AppWindow.Presenter.Kind is AppWindowPresenterKind.FullScreen)
		{
			App.MainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
			TransportControls.FullWindowSymbol.Symbol = Symbol.FullScreen;
		}
	}

	private void Root_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		ShowPlaybackChrome();
	}

	private void ShowPlaybackChrome()
	{
		TransportControls.Visibility = Visibility.Visible;
		TransportControls.Opacity = 1;
		TransportControls.IsHitTestVisible = true;
		_hideChromeTimer.Stop();
		_hideChromeTimer.Start();
	}

	private void HideChromeTimer_Tick(object? sender, object e)
	{
		_hideChromeTimer.Stop();
		TransportControls.Opacity = 0;
		TransportControls.IsHitTestVisible = false;
		TransportControls.Visibility = Visibility.Collapsed;
	}
}
