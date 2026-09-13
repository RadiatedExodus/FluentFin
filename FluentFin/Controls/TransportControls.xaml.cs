using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Input;
using CommunityToolkit.WinUI;
using FluentFin.Core;
using FluentFin.Core.Contracts.Services;
using FluentFin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ReactiveMarbles.ObservableEvents;
using ReactiveUI;


namespace FluentFin.Controls;

#nullable disable
public sealed partial class TransportControls : UserControl
{
	private readonly Subject<PointerRoutedEventArgs> _onPointerMoved = new();
	private readonly SymbolIcon _playSymbol = new(Symbol.Play);
	private readonly SymbolIcon _pauseSymbol = new(Symbol.Pause);
	private bool _isSeekingWithSlider;
	private bool _isUpdatingSliderFromPlayer;
	private bool _resumePlaybackAfterSliderSeek;
	private TimeSpan? _pendingSliderSeek;
	private DateTimeOffset _holdSliderPositionUntil;
	private int _sliderSeekVersion;

	[GeneratedDependencyProperty]
	public partial bool IsSkipButtonVisible { get; set; }

	[GeneratedDependencyProperty]
	public partial PlaylistViewModel Playlist { get; set; }


	[GeneratedDependencyProperty]
	public partial ICommand SkipCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial TrickplayViewModel Trickplay { get; set; }

	[GeneratedDependencyProperty]
	public partial IJellyfinClient JellyfinClient { get; set; }

	public IMediaPlayerController Player
	{
		get
		{
			try
			{
				return (IMediaPlayerController)GetValue(PlayerProperty);
			}
			catch
			{
				return null;
			}
		}
		set { SetValue(PlayerProperty, value); }
	}

	public static readonly DependencyProperty PlayerProperty =
		DependencyProperty.Register("Player", typeof(IMediaPlayerController), typeof(TransportControls), new PropertyMetadata(null, OnPlayerChanged));

	private static void OnPlayerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		var tc = (TransportControls)d;
		if (e.NewValue is not IMediaPlayerController controller)
		{
			return;
		}

		TimeSpan duration = TimeSpan.Zero;
		controller.DurationChanged.ObserveOn(RxApp.MainThreadScheduler).Subscribe(e =>
		{
			tc.TimeSlider.Maximum = e.TotalMilliseconds;
			duration = e;
		});
		controller.PositionChanged.ObserveOn(RxApp.MainThreadScheduler).Subscribe(e =>
		{
			try
			{
				var shouldHoldSlider = tc._isSeekingWithSlider ||
					(tc._pendingSliderSeek is not null && DateTimeOffset.Now < tc._holdSliderPositionUntil);

				if (tc._pendingSliderSeek is { } pending && Math.Abs((e - pending).TotalMilliseconds) < 1500)
				{
					tc._pendingSliderSeek = null;
					tc._holdSliderPositionUntil = DateTimeOffset.MinValue;
					shouldHoldSlider = false;
				}

				var displayPosition = shouldHoldSlider && tc._pendingSliderSeek is { } target ? target : e;
				if (!shouldHoldSlider)
				{
					tc._isUpdatingSliderFromPlayer = true;
					tc.TimeSlider.Value = e.TotalMilliseconds;
					tc._isUpdatingSliderFromPlayer = false;
				}

				tc.TxtCurrentTime.Text = Converters.Converters.TimeSpanToString(displayPosition);
				tc.TxtRemainingTime.Text = TimeRemaining(displayPosition, duration);
			}
			catch
			{
				tc._isUpdatingSliderFromPlayer = false;
			}
		});
		controller.Playing.ObserveOn(RxApp.MainThreadScheduler).Subscribe(_ => tc.PlayPauseButton.Content = tc._pauseSymbol);
		controller.Paused.ObserveOn(RxApp.MainThreadScheduler).Subscribe(_ => tc.PlayPauseButton.Content = tc._playSymbol);
		controller.VolumeChanged
			.Where(e => e >= 0)
			.Throttle(TimeSpan.FromSeconds(200))
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(e => tc.VolumeSlider.Value = Math.Floor(e));
		controller.SubtitleText.ObserveOn(RxApp.MainThreadScheduler).Subscribe(text => tc.Subtitles.Text = text);
	}

	public IObservable<Unit> OnDynamicSkip { get; }

	public TransportControls()
	{
		InitializeComponent();

		OnDynamicSkip = DynamicSkipIntroButton.Events().Click.Select(_ => Unit.Default);


		TimeSlider
			.Events()
			.ValueChanged
			.Subscribe(x =>
			{
				try
				{
					if (_isUpdatingSliderFromPlayer)
					{
						return;
					}

					_pendingSliderSeek = TimeSpan.FromMilliseconds(x.NewValue);
					TxtCurrentTime.Text = Converters.Converters.TimeSpanToString(_pendingSliderSeek.Value);
					TxtRemainingTime.Text = TimeRemaining(_pendingSliderSeek.Value, TimeSpan.FromMilliseconds(TimeSlider.Maximum));

					if (!_isSeekingWithSlider)
					{
						_ = CommitSliderSeekAfterQuietPeriod(++_sliderSeekVersion);
					}
				}
				catch { }
			});

		TimeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) =>
		{
			BeginSliderSeek();
		}), true);

		TimeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => CommitSliderSeek()), true);
		TimeSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => CommitSliderSeek()), true);
		TimeSlider.PointerCaptureLost += (_, _) => CommitSliderSeek();

		_onPointerMoved
			.ObserveOn(RxApp.MainThreadScheduler)
			.Subscribe(e =>
			{
				const int teachingTipMargin = 12;
				TrickplayTip.IsOpen = true;

				var navView = this.FindAscendantOrSelf<NavigationView>();
				var offset = navView?.IsPaneOpen == true ? navView.OpenPaneLength : 0;
				var trickplayWidth = TrickplayScrollViewer.Width + 2 * teachingTipMargin;
				var halfTrickplayWidth = trickplayWidth / 2;

				var point = e.GetCurrentPoint(TimeSlider);
				var globalPoint = e.GetCurrentPoint(this);
				Trickplay.Position = TimeSpan.FromMilliseconds((point.Position.X / TimeSlider.ActualWidth) * TimeSlider.Maximum);

				var minMargin = Math.Max(teachingTipMargin + offset, globalPoint.Position.X + offset - halfTrickplayWidth);
				var margin = Math.Min(minMargin, ActualWidth + offset - trickplayWidth - 10);
				TrickplayTip.PlacementMargin = new Thickness(margin, 0, 0, Bar.ActualHeight);
			});

		VolumeSlider.Events()
			.ValueChanged
			.Where(_ => Player is not null)
			.Subscribe(x => Player.Volume = (int)x.NewValue);
	}

	private static string TimeRemaining(TimeSpan currentTime, TimeSpan duration)
	{
		return (duration - currentTime).ToString("hh\\:mm\\:ss");
	}

	private async void SkipBackwardButton_Click(object sender, RoutedEventArgs e) => await SkipBackward();

	private async void SkipForwardButton_Click(object sender, RoutedEventArgs e) => await SkipForward();

	private async void PlayPauseButton_Click(object sender, RoutedEventArgs e) => await TogglePlayPause();

	private async void CastButton_Click(object sender, RoutedEventArgs e)
	{
		if (JellyfinClient is null)
		{
			return;
		}

		var sessions = await JellyfinClient.GetControllableSessions();

		if (sessions.FirstOrDefault(x => x.Id == SessionInfo.SessionId) is { } session && session.NowPlayingItem is { } dto)
		{
			Player.Stop();
			App.Dialogs.PlayOnSessionCommand.Execute(dto);
		}
	}

	private void TimeSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		if (Trickplay is null)
		{
			return;
		}

		if (Trickplay.Item?.Trickplay?.AdditionalData?.Count is not > 0)
		{
			return;
		}

		_onPointerMoved.OnNext(e);
	}

	private void TimeSlider_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		TrickplayTip.IsOpen = false;
	}

	private void Grid_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		TrickplayTip.IsOpen = false;
	}

	private async Task SkipBackward()
	{
		await Player.SeekBackward(JellyfinClient, TimeSpan.FromSeconds(10));
	}

	private async Task SkipForward()
	{
		await Player.SeekForward(JellyfinClient, TimeSpan.FromSeconds(30));
	}

	private async Task TogglePlayPause()
	{
		await Player.TogglePlayPlause(JellyfinClient);
	}

	private async Task CommitSliderSeekAfterQuietPeriod(int version)
	{
		await Task.Delay(150);
		if (version == _sliderSeekVersion && !_isSeekingWithSlider)
		{
			CommitSliderSeek();
		}
	}

	private void BeginSliderSeek()
	{
		if (_isSeekingWithSlider)
		{
			return;
		}

		_isSeekingWithSlider = true;
		_pendingSliderSeek = TimeSpan.FromMilliseconds(TimeSlider.Value);
		_resumePlaybackAfterSliderSeek = Player?.State is MediaPlayerState.Playing;

		if (_resumePlaybackAfterSliderSeek)
		{
			try
			{
				Player.Pause();
			}
			catch { }
		}
	}

	private void CommitSliderSeek()
	{
		if (!_isSeekingWithSlider && _pendingSliderSeek is null)
		{
			return;
		}

		_isSeekingWithSlider = false;

		if (Player is null)
		{
			return;
		}

		var position = _pendingSliderSeek ?? TimeSpan.FromMilliseconds(TimeSlider.Value);
		var shouldResume = _resumePlaybackAfterSliderSeek;
		_pendingSliderSeek = position;
		_holdSliderPositionUntil = DateTimeOffset.Now.AddSeconds(2);
		_sliderSeekVersion++;
		_resumePlaybackAfterSliderSeek = false;

		try
		{
			Player.SeekTo(position);

			if (shouldResume)
			{
				Player.Play();
			}
		}
		catch { }
	}
}

#nullable restore
