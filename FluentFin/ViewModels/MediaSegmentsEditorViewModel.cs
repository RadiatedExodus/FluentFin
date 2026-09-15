using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFin.Contracts.ViewModels;
using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Playback;
using FluentFin.Core.Settings;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.ViewModels;

public partial class MediaSegmentsEditorViewModel(
	IJellyfinClient jellyfinClient,
	ISettings settings,
	IPlaybackService playbackService) : ObservableObject, INavigationAware
{
	[ObservableProperty]
	public partial PlaylistViewModel Playlist { get; set; } = new();

	[ObservableProperty]
	public partial MediaPlayerType MediaPlayerType { get; set; }

	public ObservableCollection<MediaSegmentViewModel> Segments { get; } = [];
	public long CurrentTimeTicks => playbackService.Position.Ticks;
	public MediaSegmentViewModel? PlayingSegment { get; set; }

	public Task OnNavigatedFrom()
	{
		Playlist.PropertyChanged -= OnPlaylistPropertyChanged;
		playbackService.PlaybackChanged -= PlaybackService_PlaybackChanged;
		return playbackService.StopAsync();
	}

	public async Task OnNavigatedTo(object parameter)
	{
		if (parameter is not BaseItemDto dto)
		{
			return;
		}

		MediaPlayerType = settings.MediaPlayer;
		Playlist = dto.Type switch
		{
			BaseItemDto_Type.Movie => PlaylistViewModel.FromMovie(dto),
			BaseItemDto_Type.Episode => await PlaylistViewModel.FromEpisode(jellyfinClient, dto),
			BaseItemDto_Type.Series => await PlaylistViewModel.FromSeries(jellyfinClient, dto),
			BaseItemDto_Type.Season => await PlaylistViewModel.FromSeason(jellyfinClient, dto),
			_ => new PlaylistViewModel()
		};

		if (Playlist.Items.Count == 0)
		{
			return;
		}

		Playlist.PropertyChanged += OnPlaylistPropertyChanged;
		playbackService.PlaybackChanged += PlaybackService_PlaybackChanged;

		if (dto.Type == BaseItemDto_Type.Episode)
		{
			Playlist.SelectedItem = Playlist.Items.FirstOrDefault(x => x.Dto.IndexNumber == dto.IndexNumber && x.Dto.ParentIndexNumber == dto.ParentIndexNumber);
		}
		else
		{
			Playlist.SelectedItem = Playlist.Items.FirstOrDefault();
		}
	}

	private async void OnPlaylistPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(Playlist.SelectedItem) || Playlist.SelectedItem is not { } selectedItem)
		{
			return;
		}

		await playbackService.StopAsync();
		var full = await jellyfinClient.GetItem(selectedItem.Dto.Id ?? Guid.Empty);
		if (full is null)
		{
			return;
		}

		var index = Math.Max(0, Playlist.Items.IndexOf(selectedItem));
		var queue = Playlist.Items
			.Select((item, itemIndex) => PlaybackItem.FromDto(itemIndex == index ? full : item.Dto, PlaybackKind.Video))
			.ToList();

		await playbackService.PlayAsync(new PlaybackRequest
		{
			Kind = PlaybackKind.Video,
			StartItem = queue[index],
			QueueItems = queue,
			StartIndex = index
		});
		await playbackService.PauseAsync();

		if (await jellyfinClient.GetMediaSegments(selectedItem.Dto) is { Items.Count: > 0 } segmentsResults)
		{
			Segments.Clear();
			foreach (var segment in segmentsResults.Items.Select(MediaSegmentViewModel.FromDto))
			{
				Segments.Add(segment);
			}
		}
	}

	private async void PlaybackService_PlaybackChanged(object? sender, EventArgs e)
	{
		if (PlayingSegment is { } segment && playbackService.Position.Ticks > segment.EndTicks)
		{
			PlayingSegment = null;
			await playbackService.PauseAsync();
		}
	}

	[RelayCommand]
	private void AddSegment()
	{
		Segments.Add(new() { ItemId = Playlist.SelectedItem?.Dto?.Id });
	}

	[RelayCommand]
	private async Task DeleteSegment(MediaSegmentViewModel vm)
	{
		Segments.Remove(vm);
		if (vm.Id is { } id)
		{
			await jellyfinClient.DeleteMediaSegment(id);
		}
	}

	[RelayCommand]
	private async Task SubmitSegment(MediaSegmentViewModel vm)
	{
		if (vm.Id is { } id)
		{
			await jellyfinClient.DeleteMediaSegment(id);
		}

		await jellyfinClient.CreateMediaSegment(vm.ToDto());
	}

	[RelayCommand]
	private async Task PlaySegment(MediaSegmentViewModel vm)
	{
		PlayingSegment = vm;
		await playbackService.SeekAsync(new TimeSpan(vm.StartTicks));
		await playbackService.ResumeAsync();
	}
}

public partial class MediaSegmentViewModel : ObservableObject
{
	[ObservableProperty]
	public partial long StartTicks { get; set; }

	[ObservableProperty]
	public partial long EndTicks { get; set; }

	[ObservableProperty]
	public partial Guid? ItemId { get; set; }

	[ObservableProperty]
	public partial MediaSegmentDto_Type? Type { get; set; }

	public Guid? Id { get; set; }

	public MediaSegmentDto ToDto() => new()
	{
		StartTicks = StartTicks,
		EndTicks = EndTicks,
		ItemId = ItemId ?? Guid.NewGuid(),
		Type = Type,
		Id = Id ?? Guid.NewGuid()
	};

	public static MediaSegmentViewModel FromDto(MediaSegmentDto dto) => new()
	{
		StartTicks = dto.StartTicks ?? 0,
		EndTicks = dto.EndTicks ?? 0,
		ItemId = dto.ItemId,
		Type = dto.Type,
		Id = dto.Id
	};
}
