using Administration.Application.Abstractions;
using Administration.Application.Features.Partners.Localities;
using FluentAssertions;
using NSubstitute;
using Shared.Kernel.Primitives;

namespace Administration.Application.Tests.Features.Partners.Localities;

public sealed class SearchLocalitiesQueryHandlerTests
{
    private readonly ILocalitatiService _service = Substitute.For<ILocalitatiService>();
    private readonly SearchLocalitiesQueryHandler _handler;

    public SearchLocalitiesQueryHandlerTests()
    {
        _handler = new SearchLocalitiesQueryHandler(_service);
    }

    [Fact]
    public async Task Handle_EmptyQuery_ReturnsEmptyList()
    {
        var result = await _handler.Handle(new SearchLocalitiesQuery(""), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        await _service.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ServiceReturnsResults_MapsToDto()
    {
        var localities = new List<LocalitySearchResult>
        {
            new("Cluj-Napoca", new LocalityCounty("CJ", "Cluj"), "municipiu", 12345, "400000"),
        };

        _service.SearchAsync("Cluj", null, 10, Arg.Any<CancellationToken>())
            .Returns(Result.Success(localities));

        var result = await _handler.Handle(new SearchLocalitiesQuery("Cluj"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle();
        result.Value![0].Name.Should().Be("Cluj-Napoca");
        result.Value[0].CountyName.Should().Be("Cluj");
        result.Value[0].PostalCode.Should().Be("400000");
    }

    [Fact]
    public async Task Handle_ServiceFailure_ReturnsFailure()
    {
        var error = new Shared.Kernel.Errors.Error("Test", "Service unavailable");
        _service.SearchAsync("Cluj", null, 10, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<LocalitySearchResult>>(error));

        var result = await _handler.Handle(new SearchLocalitiesQuery("Cluj"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }
}
