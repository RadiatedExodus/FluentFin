using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using FluentFin.Contracts.Services;
using FluentFin.Core;
using FluentFin.Core.Contracts.Services;
using Flurl;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Services;

public sealed class ImageSourceCache : IImageSourceCache, IClientImageCacheMaintenance
{
	private const long MaxCacheSizeBytes = 512L * 1024 * 1024;
	private static readonly TimeSpan UntaggedTimeToLive = TimeSpan.FromHours(24);

	private readonly ILogger<ImageSourceCache> _logger;
	private readonly string _cachePath;
	private readonly HttpClient _httpClient = new();
	private readonly ConcurrentDictionary<string, Lazy<Task<Uri?>>> _downloads = [];
	private readonly Dictionary<string, BitmapImage> _cache = [];
	private readonly Queue<string> _memoryOrder = [];
	private readonly Lock _lock = new();

	public ImageSourceCache(KnownFolders knownFolders, ILogger<ImageSourceCache> logger)
	{
		_logger = logger;
		_cachePath = Path.Combine(knownFolders.ApplicationData, "ImageCache");
		Directory.CreateDirectory(_cachePath);
	}

	public BitmapImage Get(Uri uri)
	{
		var sourceUri = uri;
		if (CanCache(uri))
		{
			var cacheKey = CreateKey(uri);
			if (TryGetFreshCachedFile(cacheKey, uri, out var cachedUri))
			{
				sourceUri = cachedUri;
			}
			else
			{
				_ = GetCachedUriAsync(uri);
			}
		}

		var key = sourceUri.ToString();
		lock (_lock)
		{
			if (!_cache.TryGetValue(key, out var image))
			{
				image = new BitmapImage(sourceUri);
				_cache[key] = image;
				_memoryOrder.Enqueue(key);
				while (_memoryOrder.Count > 256)
				{
					var oldKey = _memoryOrder.Dequeue();
					_cache.Remove(oldKey);
				}
			}

			return image;
		}
	}

	public async Task<Uri?> GetCachedUriAsync(Uri uri, CancellationToken cancellationToken = default)
	{
		if (!CanCache(uri))
		{
			return uri;
		}

		var key = CreateKey(uri);
		if (TryGetFreshCachedFile(key, uri, out var cached))
		{
			_logger.LogDebug("Image cache hit. Uri={Uri}, File={File}", uri, cached.LocalPath);
			return cached;
		}

		var lazy = _downloads.GetOrAdd(key, _ => new Lazy<Task<Uri?>>(() => DownloadAsync(uri, key, CancellationToken.None)));
		try
		{
			return await lazy.Value.WaitAsync(cancellationToken);
		}
		finally
		{
			_downloads.TryRemove(key, out _);
		}
	}

	public Task<ImageCacheStats> GetStatsAsync(CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_cachePath);
		long size = 0;
		var count = 0;
		foreach (var file in Directory.EnumerateFiles(_cachePath))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var info = new FileInfo(file);
				size += info.Length;
				count++;
			}
			catch (IOException) { }
			catch (UnauthorizedAccessException) { }
		}

		return Task.FromResult(new ImageCacheStats(size, count));
	}

	async Task<ClientImageCacheStats> IClientImageCacheMaintenance.GetStatsAsync(CancellationToken cancellationToken)
	{
		var stats = await GetStatsAsync(cancellationToken);
		return new ClientImageCacheStats(stats.SizeBytes, stats.FileCount);
	}

	public Task ClearAsync(CancellationToken cancellationToken = default)
	{
		lock (_lock)
		{
			_cache.Clear();
			_memoryOrder.Clear();
		}

		Directory.CreateDirectory(_cachePath);
		foreach (var file in Directory.EnumerateFiles(_cachePath))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				File.Delete(file);
			}
			catch (IOException ex)
			{
				_logger.LogWarning(ex, "Image cache file could not be deleted. File={File}", file);
			}
			catch (UnauthorizedAccessException ex)
			{
				_logger.LogWarning(ex, "Image cache file could not be deleted. File={File}", file);
			}
		}

		_logger.LogInformation("Image cache cleared. Path={Path}", _cachePath);
		return Task.CompletedTask;
	}

	public void Clear()
	{
		lock (_lock)
		{
			_cache.Clear();
			_memoryOrder.Clear();
		}
	}

	private async Task<Uri?> DownloadAsync(Uri uri, string key, CancellationToken cancellationToken)
	{
		_logger.LogDebug("Image cache miss. Uri={Uri}", uri);
		Directory.CreateDirectory(_cachePath);
		var tempFile = Path.Combine(_cachePath, $"{key}.{Guid.NewGuid():N}.tmp");

		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, AddApiKeyIfNeeded(uri));
			using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (!response.IsSuccessStatusCode)
			{
				_logger.LogWarning("Image download failed. Uri={Uri}, StatusCode={StatusCode}", uri, response.StatusCode);
				return uri;
			}

			var extension = GetExtension(uri, response.Content.Headers.ContentType?.MediaType);
			var finalFile = Path.Combine(_cachePath, $"{key}{extension}");
			await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
			await using (var output = File.Create(tempFile))
			{
				await input.CopyToAsync(output, cancellationToken);
			}

			if (File.Exists(finalFile))
			{
				File.Delete(finalFile);
			}

			File.Move(tempFile, finalFile);
			File.SetLastAccessTimeUtc(finalFile, DateTime.UtcNow);
			_logger.LogInformation("Image cached. Uri={Uri}, File={File}, Bytes={Bytes}", uri, finalFile, new FileInfo(finalFile).Length);
			_ = Task.Run(() => PruneCache());
			return new Uri(finalFile);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Image download failed. Uri={Uri}", uri);
			return uri;
		}
		finally
		{
			try
			{
				if (File.Exists(tempFile))
				{
					File.Delete(tempFile);
				}
			}
			catch { }
		}
	}

	private bool TryGetFreshCachedFile(string key, Uri sourceUri, out Uri cachedUri)
	{
		foreach (var file in Directory.EnumerateFiles(_cachePath, $"{key}.*"))
		{
			var info = new FileInfo(file);
			if (!HasImageTag(sourceUri) && DateTime.UtcNow - info.LastWriteTimeUtc > UntaggedTimeToLive)
			{
				continue;
			}

			info.LastAccessTimeUtc = DateTime.UtcNow;
			cachedUri = new Uri(file);
			return true;
		}

		cachedUri = null!;
		return false;
	}

	private static bool CanCache(Uri uri) => uri.Scheme is "http" or "https";

	private static string CreateKey(Uri uri)
	{
		var normalizedUri = Normalize(uri);
		var identity = $"{SessionInfo.BaseUrl}|{SessionInfo.CurrentUser?.Id:N}|{normalizedUri}";
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static string Normalize(Uri uri)
	{
		var builder = new UriBuilder(uri)
		{
			Scheme = uri.Scheme.ToLowerInvariant(),
			Host = uri.Host.ToLowerInvariant()
		};

		var query = uri.Query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(part => part.Split('=', 2))
			.Where(parts => !string.Equals(Uri.UnescapeDataString(parts[0]), "ApiKey", StringComparison.OrdinalIgnoreCase))
			.OrderBy(parts => Uri.UnescapeDataString(parts[0]), StringComparer.OrdinalIgnoreCase)
			.Select(parts => parts.Length == 2 ? $"{parts[0]}={parts[1]}" : parts[0]);
		builder.Query = string.Join("&", query);
		return builder.Uri.ToString();
	}

	private static bool HasImageTag(Uri uri) =>
		uri.Query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Any(part => string.Equals(Uri.UnescapeDataString(part.Split('=', 2)[0]), "tag", StringComparison.OrdinalIgnoreCase));

	private static Uri AddApiKeyIfNeeded(Uri uri)
	{
		if (string.IsNullOrEmpty(SessionInfo.AccessToken))
		{
			return uri;
		}

		if (uri.Query.Contains("ApiKey=", StringComparison.OrdinalIgnoreCase))
		{
			return uri;
		}

		if (!string.IsNullOrEmpty(SessionInfo.BaseUrl)
			&& Uri.TryCreate(SessionInfo.BaseUrl, UriKind.Absolute, out var baseUri)
			&& !string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
		{
			return uri;
		}

		return uri.AppendQueryParam("ApiKey", SessionInfo.AccessToken).ToUri();
	}

	private static string GetExtension(Uri uri, string? mediaType)
	{
		var pathExtension = Path.GetExtension(uri.AbsolutePath);
		if (!string.IsNullOrWhiteSpace(pathExtension) && pathExtension.Length <= 5)
		{
			return pathExtension;
		}

		return mediaType?.ToLowerInvariant() switch
		{
			"image/png" => ".png",
			"image/gif" => ".gif",
			"image/webp" => ".webp",
			"image/bmp" => ".bmp",
			_ => ".jpg"
		};
	}

	private void PruneCache()
	{
		try
		{
			var files = Directory.EnumerateFiles(_cachePath)
				.Select(path => new FileInfo(path))
				.Where(info => info.Exists)
				.OrderByDescending(info => info.LastAccessTimeUtc)
				.ToList();
			var total = files.Sum(info => info.Length);
			if (total <= MaxCacheSizeBytes)
			{
				return;
			}

			foreach (var file in files.OrderBy(info => info.LastAccessTimeUtc))
			{
				if (total <= MaxCacheSizeBytes)
				{
					break;
				}

				total -= file.Length;
				file.Delete();
				_logger.LogDebug("Image cache file evicted. File={File}", file.FullName);
			}
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Image cache pruning failed");
		}
	}
}
