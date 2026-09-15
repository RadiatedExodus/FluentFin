using CommunityToolkit.WinUI;
using FluentFin.Contracts.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;


namespace FluentFin.Controls;

#nullable disable

public sealed partial class LazyLoadedImage : UserControl
{
	private readonly ILogger<LazyLoadedImage> _logger;
	private CancellationTokenSource _loadCts;
	private long _loadVersion;
	private string _currentImageUri = "";

	public static readonly DependencyProperty ImageUriProperty = DependencyProperty.Register(
		nameof(ImageUri),
		typeof(Uri),
		typeof(LazyLoadedImage),
		new PropertyMetadata(null, OnImageUriChanged));

	public LazyLoadedImage()
	{
		InitializeComponent();
		_logger = App.GetService<ILogger<LazyLoadedImage>>();

		ImageFadeIn.Completed += (object sender, object e) =>
		{
			EnableBlurHash = false;
			BlurHashImageSource = null;
		};
		Unloaded += LazyLoadedImage_Unloaded;
	}

	[GeneratedDependencyProperty]
	public partial ImageSource ImageSource { get; set; }

	[GeneratedDependencyProperty]
	public partial bool EnableBlurHash { get; set; }

	[GeneratedDependencyProperty]
	public partial WriteableBitmap BlurHashImageSource { get; set; }

	[GeneratedDependencyProperty(DefaultValue = Stretch.UniformToFill)]
	public partial Stretch Stretch { get; set; }

	[GeneratedDependencyProperty(DefaultValue = "\uE8B9")]
	public partial string Glyph { get; set; }

	public Uri ImageUri
	{
		get => GetValue(ImageUriProperty) as Uri;
		set => SetValue(ImageUriProperty, value);
	}

	private static void OnImageUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is LazyLoadedImage image)
		{
			var oldUri = e.OldValue as Uri;
			var newUri = e.NewValue as Uri;
			if (string.Equals(oldUri?.ToString(), newUri?.ToString(), StringComparison.Ordinal))
			{
				return;
			}

			image.StartImageUriLoad(newUri);
		}
	}

	private void ImageOpened(object sender, RoutedEventArgs e)
	{
		_logger.LogInformation("Lazy image opened. Uri={Uri}", _currentImageUri);
		FailedTemplate.Visibility = Visibility.Collapsed;
		ImageFadeIn.Begin();
	}

	private void ImageFailed(object sender, ExceptionRoutedEventArgs e)
	{
		_logger.LogWarning("Lazy image failed. Uri={Uri}, Error={Error}", _currentImageUri, e.ErrorMessage);
		ShowFailed();
	}

	private void ShowFailed()
	{
		Image.Opacity = 0;
		EnableBlurHash = false;
		BlurHashImageSource = null;
		FailedTemplate.Visibility = Visibility.Visible;
	}

	private void LazyLoadedImage_Unloaded(object sender, RoutedEventArgs e)
	{
		_loadCts?.Cancel();
	}

	private void StartImageUriLoad(Uri uri)
	{
		var uriText = uri?.ToString() ?? "";
		if (string.Equals(_currentImageUri, uriText, StringComparison.Ordinal))
		{
			return;
		}

		_currentImageUri = uriText;
		_loadCts?.Cancel();
		var version = Interlocked.Increment(ref _loadVersion);

		ImageFadeIn.Stop();
		Image.Opacity = 0;
		FailedTemplate.Visibility = Visibility.Collapsed;

		if (uri is null)
		{
			_logger.LogInformation("Lazy image load skipped because image URI is null. HasImageSource={HasImageSource}, HasBlurHash={HasBlurHash}",
				ImageSource is not null,
				BlurHashImageSource is not null);
			return;
		}

		_logger.LogInformation("Lazy image load started. Uri={Uri}", uri);
		var cts = new CancellationTokenSource();
		_loadCts = cts;
		_ = LoadImageUriAsync(uri, version, cts.Token);
	}

	private async Task LoadImageUriAsync(Uri uri, long version, CancellationToken cancellationToken)
	{
		try
		{
			var cache = App.GetService<IImageSourceCache>();
			var cachedUri = await cache.GetCachedUriAsync(uri, cancellationToken);
			if (cancellationToken.IsCancellationRequested || version != _loadVersion)
			{
				return;
			}

			if (cachedUri is null)
			{
				_logger.LogWarning("Lazy image cache returned no URI. Uri={Uri}", uri);
				ShowFailed();
				return;
			}

			_logger.LogInformation("Lazy image source resolved. Uri={Uri}, ResolvedUri={ResolvedUri}, IsFile={IsFile}",
				uri,
				cachedUri,
				cachedUri.IsFile);
			var imageSource = new BitmapImage(cachedUri);
			if (!DispatcherQueue.TryEnqueue(() =>
			{
				if (version == _loadVersion && !cancellationToken.IsCancellationRequested)
				{
					ImageSource = imageSource;
				}
			}))
			{
				_logger.LogWarning("Lazy image source could not be queued on the UI thread. Uri={Uri}", uri);
				ShowFailed();
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			if (version == _loadVersion)
			{
				_logger.LogWarning(ex, "Lazy image load failed before source assignment. Uri={Uri}", uri);
				ShowFailed();
			}
		}
	}
}

#nullable restore
