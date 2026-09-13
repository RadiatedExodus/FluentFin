using Microsoft.UI.Xaml.Media.Imaging;

namespace FluentFin.Contracts.Services;

public interface IImageSourceCache
{
	BitmapImage Get(Uri uri);

	void Clear();
}

