using Administration.Application.Abstractions;
using Administration.Application.Features.Partners.Localities;
using FluentAssertions;
using NSubstitute;
using Shared.Kernel.Primitives;

namespace Administration.Application.Tests.Features.Partners.Localities;

public sealed class GetCountiesQueryHandlerTests
{
    private readonly ILocalitatiService _service = Substitute.For<ILocalitatiService>();
    private readonly GetCountiesQueryHandler _handler;

    public GetCountiesQueryHandlerTests()
    {
        _handler = new GetCountiesQueryHandler(_service);
    }

    [Fact]
    public async Task Handle_ServiceReturnsCounties_MapsToDto()
    {
        var counties = new List<LocalityCounty>
        {
            new("CJ", "Cluj"),
            new("BV", "Brașov"),
        };

        _service.GetCountiesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(counties));

        var result = await _handler.Handle(new GetCountiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value[0].Should().BeEquivalentTo(new CountyDto("CJ", "Cluj"));
    }

    [Fact]
    public async Task Handle_ServiceFailure_ReturnsFailure()
    {
        var error = new Shared.Kernel.Errors.Error("Test", "Service unavailable");
        _service.GetCountiesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<List<LocalityCounty>>(error));

        var result = await _handler.Handle(new GetCountiesQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
    }
}
