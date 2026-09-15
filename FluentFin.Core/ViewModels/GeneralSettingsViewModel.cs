using FluentFin.Core.Contracts.Services;
using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Core.ViewModels;

public partial class GeneralSettingsViewModel(IJellyfinClient jellyfinClient, IClientImageCacheMaintenance imageCache) : ServerConfigurationViewModel(jellyfinClient)
{
	protected override List<JellyfinConfigItemViewModel> CreateItems(ServerConfiguration config)
	{
		return
		[
			new JellyfinConfigItemViewModel<string>(() => config.ServerName ?? string.Empty, value => config.ServerName = value)
			{
				DisplayName = "Server name",
				Description = "This name will be used to identify the server and will default to the server's hostname."
			},
			new JellyfinGroupedConfigItemViewModel()
			{
				DisplayName = "Paths",
				Items =
				[
					new JellyfinTextBlockConfigItemViewModel(() => config.CachePath ?? string.Empty, value => config.CachePath = value)
					{
						DisplayName = "Cache path",
						Description = "Specify a custom location for server cache files such as images. Leave blank to use the server default."
					},
					new JellyfinTextBlockConfigItemViewModel(() => config.MetadataPath ?? string.Empty, value => config.MetadataPath = value)
					{
						DisplayName = "Metadata path",
						Description = "Specify a custom location for downloaded artwork and metadata."
					},
				]
			},
			new JellyfinConfigItemViewModel<bool>(() => config.QuickConnectAvailable ?? false, value => config.QuickConnectAvailable = value)
			{
				DisplayName = "Enable quick connect",
				Description = "Enable quick connect to allow easy access to your server from anywhere."
			},
			new JellyfinGroupedConfigItemViewModel()
			{
				DisplayName = "Performance",
				Items =
				[
					new JellyfinConfigItemViewModel<double>(() => config.LibraryScanFanoutConcurrency ?? 0, value => config.LibraryScanFanoutConcurrency = (int)value)
					{
						DisplayName = "Parallel library scan task limit",
						Description = "Maximum number of parallel tasks during library scans. Setting this to 0 will choose a limit based on your systems core count. WARNING: Setting this number too high may cause issues with network file systems; if you encounter problems lower this number"
					},
					new JellyfinConfigItemViewModel<double>(() => config.ParallelImageEncodingLimit ?? 0, value => config.ParallelImageEncodingLimit = (int)value)
					{
						DisplayName = "Parallel image encoding limit",
						Description = "Maximum number of image encodings that are allowed to run in parallel. Setting this to 0 will choose a limit based on your systems core count."
					}
				]
			},
			new JellyfinGroupedConfigItemViewModel()
			{
				DisplayName = "Client Cache",
				Description = "Local FluentFin cache files used to speed up artwork loading.",
				Items =
				[
					new JellyfinTextBlockConfigItemViewModel(GetImageCacheSummary, _ => { })
					{
						DisplayName = "Image cache",
						Description = "Artwork stored locally on this device."
					},
					new JellyfinActionConfigItemViewModel(ClearImageCache)
					{
						DisplayName = "Clear image cache",
						Description = "Remove locally cached artwork. Images will redownload when needed.",
						ButtonText = "Clear"
					}
				]
			}
		];
	}

	private string GetImageCacheSummary()
	{
		try
		{
			var stats = imageCache.GetStatsAsync().GetAwaiter().GetResult();
			return $"{stats.FileCount} files, {FormatBytes(stats.SizeBytes)}";
		}
		catch
		{
			return "Unavailable";
		}
	}

	private async Task ClearImageCache()
	{
		await imageCache.ClearAsync();
		var configuration = await _jellyfinClient.GetConfiguration();
		if (configuration is not null)
		{
			Items = CreateItems(configuration);
		}
	}

	private static string FormatBytes(long bytes)
	{
		string[] units = ["B", "KB", "MB", "GB"];
		double value = bytes;
		var unit = 0;
		while (value >= 1024 && unit < units.Length - 1)
		{
			value /= 1024;
			unit++;
		}

		return $"{value:0.#} {units[unit]}";
	}
}
