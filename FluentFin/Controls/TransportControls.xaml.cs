using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.WinUI;
using FluentFin.Core.Playback;
using FluentFin.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FluentFin.Controls;

public sealed partial class TransportControls : UserControl
{
	private readonly SymbolIcon _playSymbol = new(Symbol.Play);
	private readonly SymbolIcon _pauseSymbol = new(Symbol.Pause);
	private bool _isSeekingWithSlider;
	private bool _isUpdatingSliderFromState;
	private bool _resumePlaybackAfterSliderSeek;
	private TimeSpan? _pendingSliderSeek;
	private DateTimeOffset _holdSliderPositionUntil;
	private ObservableCollection<AudioTrack>? _subscribedAudioTracks;
	private ObservableCollection<SubtitleTrack>? _subscribedSubtitleTracks;

	[GeneratedDependencyProperty]
	public partial bool IsSkipButtonVisible { get; set; }

	[GeneratedDependencyProperty]
	public partial PlaylistViewModel? Playlist { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial TrickplayViewModel? Trickplay { get; set; }

	[GeneratedDependencyProperty]
	public partial PlaybackState PlaybackState { get; set; }

	[GeneratedDependencyProperty]
	public partial TimeSpan Position { get; set; }

	[GeneratedDependencyProperty]
	public partial TimeSpan Duration { get; set; }

	[GeneratedDependencyProperty]
	public partial double Volume { get; set; }

	[GeneratedDependencyProperty]
	public partial string? SubtitleText { get; set; }

	[GeneratedDependencyProperty]
	public partial ObservableCollection<AudioTrack>? AudioTracks { get; set; }

	[GeneratedDependencyProperty]
	public partial ObservableCollection<SubtitleTrack>? SubtitleTracks { get; set; }

	[GeneratedDependencyProperty]
	public partial int? SelectedAudioTrackIndex { get; set; }

	[GeneratedDependencyProperty]
	public partial int? SelectedSubtitleTrackIndex { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? TogglePlayPauseCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SeekCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipBackwardCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipForwardCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipNextCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SkipPreviousCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SetVolumeCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? StopCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? CastCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? ToggleFullscreenCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SelectAudioTrackCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? SelectSubtitleTrackCommand { get; set; }

	[GeneratedDependencyProperty]
	public partial ICommand? DisableSubtitlesCommand { get; set; }

	public TransportControls()
	{
		InitializeComponent();
		TimeSlider.ValueChanged += TimeSlider_ValueChanged;
		TimeSlider.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => BeginSliderSeek()), true);
		TimeSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => CommitSliderSeek()), true);
		TimeSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, _) => CommitSliderSeek()), true);
		TimeSlider.PointerCaptureLost += (_, _) => CommitSliderSeek();
		VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
	}

	partial void OnPlaybackStateChanged(PlaybackState newValue) => UpdatePlayPauseIcon();
	partial void OnPositionChanged(TimeSpan newValue) => UpdatePosition(newValue);
	partial void OnDurationChanged(TimeSpan newValue)
	{
		TimeSlider.Maximum = Math.Max(0, newValue.TotalMilliseconds);
		UpdatePosition(Position);
	}

	partial void OnVolumeChanged(double newValue)
	{
		VolumeSlider.Value = Math.Clamp(newValue, 0, 1) * 100;
	}

	partial void OnSubtitleTextChanged(string? newValue)
	{
		Subtitles.Text = newValue ?? "";
		Subtitles.Visibility = string.IsNullOrWhiteSpace(newValue) ? Visibility.Collapsed : Visibility.Visible;
	}

	partial void OnAudioTracksChanged(ObservableCollection<AudioTrack>? newValue)
	{
		if (_subscribedAudioTracks is not null)
		{
			_subscribedAudioTracks.CollectionChanged -= AudioTracks_CollectionChanged;
		}

		_subscribedAudioTracks = newValue;
		if (_subscribedAudioTracks is not null)
		{
			_subscribedAudioTracks.CollectionChanged += AudioTracks_CollectionChanged;
		}

		RefreshAudioFlyout();
	}

	partial void OnSubtitleTracksChanged(ObservableCollection<SubtitleTrack>? newValue)
	{
		if (_subscribedSubtitleTracks is not null)
		{
			_subscribedSubtitleTracks.CollectionChanged -= SubtitleTracks_CollectionChanged;
		}

		_subscribedSubtitleTracks = newValue;
		if (_subscribedSubtitleTracks is not null)
		{
			_subscribedSubtitleTracks.CollectionChanged += SubtitleTracks_CollectionChanged;
		}

		RefreshSubtitleFlyout();
	}

	partial void OnSelectedAudioTrackIndexChanged(int? newValue) => RefreshAudioFlyout();
	partial void OnSelectedSubtitleTrackIndexChanged(int? newValue) => RefreshSubtitleFlyout();

	private void AudioTracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshAudioFlyout();
	private void SubtitleTracks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSubtitleFlyout();

	private static string TimeRemaining(TimeSpan currentTime, TimeSpan duration)
	{
		var remaining = duration - currentTime;
		if (remaining < TimeSpan.Zero)
		{
			remaining = TimeSpan.Zero;
		}

		return remaining.ToString("hh\\:mm\\:ss");
	}

	private void UpdatePlayPauseIcon()
	{
		PlayPauseButton.Content = PlaybackState is PlaybackState.Playing ? _pauseSymbol : _playSymbol;
	}

	private void UpdatePosition(TimeSpan position)
	{
		var shouldHoldSlider = _isSeekingWithSlider ||
			(_pendingSliderSeek is not null && DateTimeOffset.Now < _holdSliderPositionUntil);

		if (_pendingSliderSeek is { } pending && Math.Abs((position - pending).TotalMilliseconds) < 1500)
		{
			_pendingSliderSeek = null;
			_holdSliderPositionUntil = DateTimeOffset.MinValue;
			shouldHoldSlider = false;
		}

		var displayPosition = shouldHoldSlider && _pendingSliderSeek is { } target ? target : position;
		if (!shouldHoldSlider)
		{
			_isUpdatingSliderFromState = true;
			TimeSlider.Value = Math.Clamp(position.TotalMilliseconds, 0, TimeSlider.Maximum);
			_isUpdatingSliderFromState = false;
		}

		TxtCurrentTime.Text = Converters.Converters.TimeSpanToString(displayPosition);
		TxtRemainingTime.Text = TimeRemaining(displayPosition, Duration);
	}

	private void RefreshAudioFlyout()
	{
		if (AudioTracks is not { Count: > 0 } audioTracks)
		{
			AudioSelectionButton.Flyout = null;
			AudioSelectionButton.Visibility = Visibility.Collapsed;
			return;
		}

		var flyout = new MenuFlyout();
		foreach (var track in audioTracks)
		{
			flyout.Items.Add(new MenuFlyoutItem
			{
				Text = string.Join(" - ", new[] { track.Language, track.Name }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
				Command = SelectAudioTrackCommand,
				CommandParameter = track.Id,
				Icon = track.Id == SelectedAudioTrackIndex ? new SymbolIcon(Symbol.Accept) : null
			});
		}

		AudioSelectionButton.Flyout = flyout;
		AudioSelectionButton.Visibility = Visibility.Visible;
	}

	private void RefreshSubtitleFlyout()
	{
		if (SubtitleTracks is not { Count: > 0 } subtitleTracks)
		{
			CCSelectionButton.Flyout = null;
			CCSelectionButton.Visibility = Visibility.Collapsed;
			return;
		}

		var flyout = new MenuFlyout();
		flyout.Items.Add(new MenuFlyoutItem
		{
			Text = "Off",
			Command = DisableSubtitlesCommand,
			Icon = SelectedSubtitleTrackIndex is null ? new SymbolIcon(Symbol.Accept) : null
		});

		foreach (var track in subtitleTracks)
		{
			flyout.Items.Add(new MenuFlyoutItem
			{
				Text = string.Join(" - ", new[] { track.Language, track.Name }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim(),
				Command = SelectSubtitleTrackCommand,
				CommandParameter = track.Id,
				Icon = track.Id == SelectedSubtitleTrackIndex ? new SymbolIcon(Symbol.Accept) : null
			});
		}

		CCSelectionButton.Flyout = flyout;
		CCSelectionButton.Visibility = Visibility.Visible;
	}

	private async void SkipBackwardButton_Click(object sender, RoutedEventArgs e)
	{
		if (SkipBackwardCommand?.CanExecute(null) == true)
		{
			SkipBackwardCommand.Execute(null);
		}
		await Task.CompletedTask;
	}

	private async void SkipForwardButton_Click(object sender, RoutedEventArgs e)
	{
		if (SkipForwardCommand?.CanExecute(null) == true)
		{
			SkipForwardCommand.Execute(null);
		}
		await Task.CompletedTask;
	}

	private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
	{
		if (TogglePlayPauseCommand?.CanExecute(null) == true)
		{
			TogglePlayPauseCommand.Execute(null);
		}
	}

	private void CastButton_Click(object sender, RoutedEventArgs e)
	{
		if (CastCommand?.CanExecute(null) == true)
		{
			CastCommand.Execute(null);
		}
	}

	private void FullWindowButton_Click(object sender, RoutedEventArgs e)
	{
		if (ToggleFullscreenCommand?.CanExecute(null) == true)
		{
			ToggleFullscreenCommand.Execute(null);
		}
	}

	private void TimeSlider_PointerEntered(object sender, PointerRoutedEventArgs e)
	{
		if (Trickplay is null || Trickplay.Item?.Trickplay?.AdditionalData?.Count is not > 0)
		{
			return;
		}

		TrickplayTip.IsOpen = true;
		var point = e.GetCurrentPoint(TimeSlider);
		Trickplay.Position = TimeSpan.FromMilliseconds((point.Position.X / TimeSlider.ActualWidth) * TimeSlider.Maximum);
		TrickplayTip.PlacementMargin = new Thickness(Math.Max(12, point.Position.X - 120), 0, 0, Bar.ActualHeight);
	}

	private void TimeSlider_PointerExited(object sender, PointerRoutedEventArgs e)
	{
		TrickplayTip.IsOpen = false;
	}

	private void Grid_PointerMoved(object sender, PointerRoutedEventArgs e)
	{
		TrickplayTip.IsOpen = false;
	}

	private void TimeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (_isUpdatingSliderFromState || !_isSeekingWithSlider)
		{
			return;
		}

		_pendingSliderSeek = TimeSpan.FromMilliseconds(e.NewValue);
		TxtCurrentTime.Text = Converters.Converters.TimeSpanToString(_pendingSliderSeek.Value);
		TxtRemainingTime.Text = TimeRemaining(_pendingSliderSeek.Value, Duration);
	}

	private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
	{
		if (SetVolumeCommand?.CanExecute(e.NewValue / 100d) == true)
		{
			SetVolumeCommand.Execute(e.NewValue / 100d);
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
		_resumePlaybackAfterSliderSeek = PlaybackState is PlaybackState.Playing;
		if (_resumePlaybackAfterSliderSeek && TogglePlayPauseCommand?.CanExecute(null) == true)
		{
			TogglePlayPauseCommand.Execute(null);
		}
	}

	private void CommitSliderSeek()
	{
		if (!_isSeekingWithSlider && _pendingSliderSeek is null)
		{
			return;
		}

		_isSeekingWithSlider = false;
		var position = _pendingSliderSeek ?? TimeSpan.FromMilliseconds(TimeSlider.Value);
		var shouldResume = _resumePlaybackAfterSliderSeek;
		_pendingSliderSeek = position;
		_holdSliderPositionUntil = DateTimeOffset.Now.AddSeconds(2);
		_resumePlaybackAfterSliderSeek = false;

		if (SeekCommand?.CanExecute(position) == true)
		{
			SeekCommand.Execute(position);
		}

		if (shouldResume && TogglePlayPauseCommand?.CanExecute(null) == true)
		{
			TogglePlayPauseCommand.Execute(null);
		}
	}
}
