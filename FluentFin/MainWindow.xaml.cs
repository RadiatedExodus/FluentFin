using FluentFin.Contracts.Services;
using FluentFin.Controls;
using FluentFin.Core;
using FluentFin.Core.ViewModels;
using FluentFin.Playback.Presentation;
using FluentFin.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;
using Windows.UI;

namespace FluentFin;

public sealed partial class MainWindow : WindowEx
{
	private readonly IPlaybackPresentationManager _playbackPresentationManager;
	private readonly ILogger<MainWindow> _logger;
	private VideoPlayerPage? _videoPlayerPage;
	private MusicMiniPlayer? _musicMiniPlayer;
	private MusicExpandedPlayer? _musicExpandedPlayer;
	private MusicQueuePanel? _musicQueuePanel;
	private int _activeVideoRequestId;
	private int _playbackPresentationUpdateQueued;
	private int _musicPresentationAnimationVersion;
	private CancellationTokenSource? _immersiveChromeHideCts;
	private bool _captionButtonsAreVisible = true;

	public IMainWindowViewModel ViewModel { get; } = App.GetService<IMainWindowViewModel>();

	public MainWindow()
	{
		_playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		_logger = App.GetService<ILogger<MainWindow>>();
		InitializeComponent();
		ExtendsContentIntoTitleBar = true;
		AppWindow.SetIcon("Assets/jellyfin.ico");
		SetCaptionButtonsVisible(true);

		App.GetKeyedService<INavigationService>(NavigationRegions.InitialSetup).Frame = RootFrame;
		_playbackPresentationManager.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName is nameof(IPlaybackPresentationManager.Mode) or nameof(IPlaybackPresentationManager.MusicMode))
			{
				UpdateTopChromeMode();
				QueuePlaybackPresentationUpdate();
			}
		};

		RootGrid.KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.Left, VirtualKeyModifiers.Menu));
		RootGrid.KeyboardAccelerators.Add(BuildKeyboardAccelerator(VirtualKey.GoBack));
		UpdateTopChromeMode();
	}

	private static KeyboardAccelerator BuildKeyboardAccelerator(VirtualKey key, VirtualKeyModifiers? modifiers = null)
	{
		var keyboardAccelerator = new KeyboardAccelerator() { Key = key };

		if (modifiers.HasValue)
		{
			keyboardAccelerator.Modifiers = modifiers.Value;
		}

		keyboardAccelerator.Invoked += OnKeyboardAcceleratorInvoked;

		return keyboardAccelerator;
	}

	private void QueuePlaybackPresentationUpdate()
	{
		if (Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 1) == 1)
		{
			return;
		}

		if (DispatcherQueue.HasThreadAccess)
		{
			_ = UpdatePlaybackPresentationAsync();
			return;
		}

		if (!DispatcherQueue.TryEnqueue(() => _ = UpdatePlaybackPresentationAsync()))
		{
			Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 0);
		}
	}

	private static void OnKeyboardAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
	{
		var playbackPresentationManager = App.GetService<IPlaybackPresentationManager>();
		if (playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay ||
			playbackPresentationManager.MusicMode is MusicPresentationMode.Expanded or MusicPresentationMode.Queue)
		{
			_ = playbackPresentationManager.HideAsync();
			args.Handled = true;
		}
	}

	private async Task UpdatePlaybackPresentationAsync()
	{
		try
		{
			Interlocked.Exchange(ref _playbackPresentationUpdateQueued, 0);

			if (_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay &&
				_playbackPresentationManager.VideoOverlay is { } state)
			{
				_logger.LogInformation("Activating full-window video playback overlay. RequestId={RequestId}, ParameterType={ParameterType}",
					state.RequestId,
					state.Parameter?.GetType().FullName ?? "<null>");
				Interlocked.Increment(ref _musicPresentationAnimationVersion);
				PlaybackPresenter.Visibility = Visibility.Visible;
				MusicOverlayPresenter.Visibility = Visibility.Collapsed;
				MusicMiniPlayerPresenter.Visibility = Visibility.Collapsed;
				MusicOverlayPresenter.Content = null;
				MusicMiniPlayerPresenter.Content = null;
				RootFrame.Margin = new Thickness(0);

				if (_activeVideoRequestId != state.RequestId)
				{
					if (_videoPlayerPage is not null)
					{
						await _videoPlayerPage.DeactivateAsync();
					}

					_videoPlayerPage = new VideoPlayerPage();
					PlaybackPresenter.Content = _videoPlayerPage;
					_activeVideoRequestId = state.RequestId;
					await _videoPlayerPage.ActivateAsync(state.Parameter);
				}

				return;
			}

			if (_videoPlayerPage is not null)
			{
				await _videoPlayerPage.DeactivateAsync();
				_videoPlayerPage = null;
				_activeVideoRequestId = 0;
			}

			PlaybackPresenter.Content = null;
			PlaybackPresenter.Visibility = Visibility.Collapsed;
			await UpdateMusicPresentationAsync();
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to update playback presentation. Mode={Mode}", _playbackPresentationManager.Mode);
		}
	}

	private async Task UpdateMusicPresentationAsync()
	{
		var version = Interlocked.Increment(ref _musicPresentationAnimationVersion);

		switch (_playbackPresentationManager.MusicMode)
		{
			case MusicPresentationMode.Expanded:
				_musicExpandedPlayer ??= new MusicExpandedPlayer();
				Grid.SetRow(MusicOverlayPresenter, 0);
				Grid.SetRowSpan(MusicOverlayPresenter, 2);
				RootFrame.Margin = new Thickness(0);
				await HideMiniPlayerAsync(version);
				await ShowOverlayAsync(_musicExpandedPlayer, version);
				break;
			case MusicPresentationMode.Queue:
				_musicQueuePanel ??= new MusicQueuePanel();
				_musicMiniPlayer ??= new MusicMiniPlayer();
				Grid.SetRow(MusicOverlayPresenter, 1);
				Grid.SetRowSpan(MusicOverlayPresenter, 1);
				RootFrame.Margin = new Thickness(0, 0, 0, 96);
				await ShowOverlayAsync(_musicQueuePanel, version);
				await ShowMiniPlayerAsync(version);
				break;
			case MusicPresentationMode.Compact:
				_musicMiniPlayer ??= new MusicMiniPlayer();
				Grid.SetRow(MusicOverlayPresenter, 1);
				Grid.SetRowSpan(MusicOverlayPresenter, 1);
				RootFrame.Margin = new Thickness(0, 0, 0, 96);
				await HideOverlayAsync(version);
				await ShowMiniPlayerAsync(version);
				break;
			default:
				Grid.SetRow(MusicOverlayPresenter, 1);
				Grid.SetRowSpan(MusicOverlayPresenter, 1);
				RootFrame.Margin = new Thickness(0);
				await HideOverlayAsync(version);
				await HideMiniPlayerAsync(version);
				break;
		}
	}

	private async Task ShowMiniPlayerAsync(int version)
	{
		if (version != _musicPresentationAnimationVersion)
		{
			return;
		}

		MusicMiniPlayerPresenter.Content = _musicMiniPlayer;
		MusicMiniPlayerPresenter.Visibility = Visibility.Visible;
		MusicMiniPlayerPresenter.IsHitTestVisible = true;
		await RunTransitionAsync(MusicMiniPlayerPresenter, MusicMiniPlayerTransform, 1, 0, scale: null, TimeSpan.FromMilliseconds(200));
	}

	private async Task HideMiniPlayerAsync(int version)
	{
		if (MusicMiniPlayerPresenter.Visibility is not Visibility.Visible)
		{
			MusicMiniPlayerPresenter.Content = null;
			return;
		}

		MusicMiniPlayerPresenter.IsHitTestVisible = false;
		await RunTransitionAsync(MusicMiniPlayerPresenter, MusicMiniPlayerTransform, 0, 96, scale: null, TimeSpan.FromMilliseconds(180));
		if (version == _musicPresentationAnimationVersion)
		{
			MusicMiniPlayerPresenter.Content = null;
			MusicMiniPlayerPresenter.Visibility = Visibility.Collapsed;
		}
	}

	private async Task ShowOverlayAsync(UIElement content, int version)
	{
		if (version != _musicPresentationAnimationVersion)
		{
			return;
		}

		MusicOverlayPresenter.Content = content;
		MusicOverlayPresenter.Visibility = Visibility.Visible;
		MusicOverlayPresenter.IsHitTestVisible = true;
		await RunTransitionAsync(MusicOverlayPresenter, MusicOverlayTransform, 1, translateY: null, scale: 1, TimeSpan.FromMilliseconds(200));
	}

	private async Task HideOverlayAsync(int version)
	{
		if (MusicOverlayPresenter.Visibility is not Visibility.Visible)
		{
			MusicOverlayPresenter.Content = null;
			return;
		}

		MusicOverlayPresenter.IsHitTestVisible = false;
		await RunTransitionAsync(MusicOverlayPresenter, MusicOverlayTransform, 0, translateY: null, scale: 0.96, TimeSpan.FromMilliseconds(180));
		if (version == _musicPresentationAnimationVersion)
		{
			MusicOverlayPresenter.Content = null;
			MusicOverlayPresenter.Visibility = Visibility.Collapsed;
		}
	}

	private static Task RunTransitionAsync(UIElement target, DependencyObject transform, double opacity, double? translateY, double? scale, TimeSpan duration)
	{
		var storyboard = new Storyboard();
		storyboard.Children.Add(CreateDoubleAnimation(target, "Opacity", opacity, duration));

		if (translateY is { } y)
		{
			storyboard.Children.Add(CreateDoubleAnimation(transform, "Y", y, duration));
		}

		if (scale is { } scaleValue)
		{
			storyboard.Children.Add(CreateDoubleAnimation(transform, "ScaleX", scaleValue, duration));
			storyboard.Children.Add(CreateDoubleAnimation(transform, "ScaleY", scaleValue, duration));
		}

		var completion = new TaskCompletionSource();
		storyboard.Completed += (_, _) => completion.TrySetResult();
		storyboard.Begin();
		return completion.Task;
	}

	private static DoubleAnimation CreateDoubleAnimation(DependencyObject target, string property, double to, TimeSpan duration)
	{
		var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
		var animation = new DoubleAnimation
		{
			To = to,
			Duration = new Duration(duration),
			EasingFunction = easing,
			EnableDependentAnimation = true
		};
		Storyboard.SetTarget(animation, target);
		Storyboard.SetTargetProperty(animation, property);
		return animation;
	}

	private void UpdateTopChromeMode()
	{
		var isImmersive =
			_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay ||
			_playbackPresentationManager.MusicMode is MusicPresentationMode.Expanded;

		ViewModel.TitleBarViewModel.IsOverlayChromeMode = isImmersive;
		ImmersiveTopChromeHotZone.IsHitTestVisible = isImmersive;

		if (isImmersive)
		{
			HideImmersiveChromeNow();
			return;
		}

		_immersiveChromeHideCts?.Cancel();
		ViewModel.TitleBarViewModel.IsVisible = true;
		SetCaptionButtonsVisible(true);
	}

	private void ImmersiveTopChrome_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		if (!IsImmersiveChromeActive())
		{
			return;
		}

		_immersiveChromeHideCts?.Cancel();
		ViewModel.TitleBarViewModel.IsVisible = true;
		SetCaptionButtonsVisible(true);
	}

	private void ImmersiveTopChrome_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		if (IsImmersiveChromeActive())
		{
			ScheduleImmersiveChromeHide();
		}
	}

	private void HideImmersiveChromeNow()
	{
		_immersiveChromeHideCts?.Cancel();
		ViewModel.TitleBarViewModel.IsVisible = false;
		SetCaptionButtonsVisible(false);
	}

	private void ScheduleImmersiveChromeHide()
	{
		_immersiveChromeHideCts?.Cancel();
		var cts = new CancellationTokenSource();
		_immersiveChromeHideCts = cts;
		_ = HideImmersiveChromeAfterDelay(cts.Token);
	}

	private async Task HideImmersiveChromeAfterDelay(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(TimeSpan.FromMilliseconds(1400), cancellationToken);
			if (!cancellationToken.IsCancellationRequested && IsImmersiveChromeActive())
			{
				ViewModel.TitleBarViewModel.IsVisible = false;
				SetCaptionButtonsVisible(false);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		if (!IsImmersiveChromeActive())
		{
			return;
		}

		var position = e.GetCurrentPoint(RootGrid).Position;
		if (position.Y <= 72)
		{
			_immersiveChromeHideCts?.Cancel();
			ViewModel.TitleBarViewModel.IsVisible = true;
			SetCaptionButtonsVisible(true);
			return;
		}

		if (ViewModel.TitleBarViewModel.IsVisible)
		{
			ScheduleImmersiveChromeHide();
		}
	}

	private void RootGrid_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		if (IsImmersiveChromeActive())
		{
			ScheduleImmersiveChromeHide();
		}
	}

	private void SetCaptionButtonsVisible(bool isVisible)
	{
		if (_captionButtonsAreVisible == isVisible)
		{
			return;
		}

		_captionButtonsAreVisible = isVisible;
		var transparent = Color.FromArgb(0, 0, 0, 0);
		var color = isVisible ? Color.FromArgb(255, 255, 255, 255) : transparent;
		var hoverBackground = isVisible ? Color.FromArgb(32, 255, 255, 255) : transparent;
		var pressedBackground = isVisible ? Color.FromArgb(48, 255, 255, 255) : transparent;
		var inactiveColor = isVisible ? Color.FromArgb(160, 255, 255, 255) : transparent;

		AppWindow.TitleBar.ButtonForegroundColor = color;
		AppWindow.TitleBar.ButtonHoverForegroundColor = color;
		AppWindow.TitleBar.ButtonPressedForegroundColor = color;
		AppWindow.TitleBar.ButtonInactiveForegroundColor = inactiveColor;
		AppWindow.TitleBar.ButtonBackgroundColor = transparent;
		AppWindow.TitleBar.ButtonInactiveBackgroundColor = transparent;
		AppWindow.TitleBar.ButtonHoverBackgroundColor = hoverBackground;
		AppWindow.TitleBar.ButtonPressedBackgroundColor = pressedBackground;
	}

	private bool IsImmersiveChromeActive() =>
		_playbackPresentationManager.Mode is PlaybackPresentationMode.VideoOverlay ||
		_playbackPresentationManager.MusicMode is MusicPresentationMode.Expanded;
}
