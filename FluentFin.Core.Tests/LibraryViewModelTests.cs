using FluentFin.Core.Contracts.Services;
using FluentFin.Core.Services;
using FluentFin.Core.ViewModels;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FluentFin.Core.Tests;

public class LibraryViewModelTests
{
	[Fact]
	public async Task OnNavigatedTo_UsesServerTotalRecordCountForPageCount()
	{
		var library = new BaseItemDto
		{
			Id = Guid.NewGuid(),
			Type = BaseItemDto_Type.CollectionFolder,
			CollectionType = BaseItemDto_CollectionType.Movies,
		};

		var client = new Mock<IJellyfinClient>();
		client.Setup(x => x.GetItems(It.IsAny<ItemQuery>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new PagedResult<BaseItemDto>
			{
				Items =
				[
					new BaseItemDto { Id = Guid.NewGuid(), Name = "One", Type = BaseItemDto_Type.Movie },
				],
				StartIndex = 0,
				TotalRecordCount = 250,
			});
		client.Setup(x => x.GetFilters(library))
			.ReturnsAsync((QueryFiltersLegacy?)null);

		var viewModel = new LibraryViewModel(client.Object, NullLogger<LibraryViewModel>.Instance);

		await viewModel.OnNavigatedTo(library);

		Assert.Single(viewModel.Items);
		Assert.Equal(3, viewModel.NumberOfPages);
		client.Verify(x => x.GetItems(It.Is<ItemQuery>(query =>
			query.ParentId == library.Id
			&& query.StartIndex == 0
			&& query.Limit == 100
			&& query.IncludeItemTypes.SequenceEqual(new[] { BaseItemKind.Movie })),
			It.IsAny<CancellationToken>()), Times.Once);
	}
}
