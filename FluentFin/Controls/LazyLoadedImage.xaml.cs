using CommunityToolkit.WinUI;
using FluentFin.Contracts.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;


namespace FluentFin.Controls;

#nullable disable

public sealed partial class LazyLoadedImage : UserControl
{
	private CancellationTokenSource _loadCts;
	private long _loadVersion;

	public static readonly DependencyProperty ImageUriProperty = DependencyProperty.Register(
		nameof(ImageUri),
		typeof(Uri),
		typeof(LazyLoadedImage),
		new PropertyMetadata(null, OnImageUriChanged));

	public LazyLoadedImage()
	{
		InitializeComponent();

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
			image.StartImageUriLoad(e.NewValue as Uri);
		}
	}

	private void ImageOpened(object sender, RoutedEventArgs e)
	{
		FailedTemplate.Visibility = Visibility.Collapsed;
		ImageFadeIn.Begin();
	}

	private void ImageFailed(object sender, ExceptionRoutedEventArgs e)
	{
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
		_loadCts?.Cancel();
		var version = Interlocked.Increment(ref _loadVersion);

		ImageFadeIn.Stop();
		Image.Opacity = 0;
		FailedTemplate.Visibility = Visibility.Collapsed;

		if (uri is null)
		{
			Image.Source = ImageSource;
			return;
		}

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
				ShowFailed();
				return;
			}

			Image.Source = new BitmapImage(cachedUri);
		}
		catch (OperationCanceledException) { }
		catch
		{
			if (version == _loadVersion)
			{
				ShowFailed();
			}
		}
	}
}

#nullable restore
