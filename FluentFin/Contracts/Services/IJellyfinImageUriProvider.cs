using Jellyfin.Sdk.Generated.Models;

namespace FluentFin.Contracts.Services;

public interface IJellyfinImageUriProvider
{
	Uri? GetImageUri(BaseItemDto? item, ImageType imageType, double height);

	Uri? GetImageUri(BaseItemPerson? person, double height);

	Uri? GetImageUri(VirtualFolderInfo? folder);
}
