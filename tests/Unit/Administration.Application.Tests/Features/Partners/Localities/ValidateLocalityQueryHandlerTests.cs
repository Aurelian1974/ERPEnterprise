using Administration.Application.Abstractions;
using Administration.Application.Features.PartnerTypes;
using Administration.Application.Features.Partners.Localities;
using FluentAssertions;
using NSubstitute;
using Shared.Kernel.Primitives;

namespace Administration.Application.Tests.Features.Partners.Localities;

public sealed class ValidateLocalityQueryHandlerTests
{
    private readonly ILocalitatiService _service = Substitute.For<ILocalitatiService>();
    private readonly ValidateLocalityQueryHandler _handler;

    public ValidateLocalityQueryHandlerTests()
    {
        _handler = new ValidateLocalityQueryHandler(_service);
    }

    [Fact]
    public async Task Handle_EmptyName_ReturnsInvalid()
    {
        var result = await _handler.Handle(new ValidateLocalityQuery(""), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Valid.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_InvalidLocality_ReturnsFailure()
    {
        var validation = new LocalityValidationResult(false, 0, null);
        _service.ValidateAsync("X", "CJ", Arg.Any<CancellationToken>())
            .Returns(Result.Success(validation));

        var result = await _handler.Handle(new ValidateLocalityQuery("X", "CJ"), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Administration.LocalityValidationFailed");
    }

    [Fact]
    public async Task Handle_ValidLocality_ReturnsMatch()
    {
        var match = new LocalityValidationMatch(
            "Cluj-Napoca",
            new LocalityCounty("CJ", "Cluj"),
            "municipiu",
            12345,
            "400000");
        var validation = new LocalityValidationResult(true, 0.95, match);

        _service.ValidateAsync("Cluj", null, Arg.Any<CancellationToken>())
            .Returns(Result.Success(validation));

        var result = await _handler.Handle(new ValidateLocalityQuery("Cluj"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Valid.Should().BeTrue();
        result.Value.Match.Should().NotBeNull();
        result.Value.Match!.Name.Should().Be("Cluj-Napoca");
        result.Value.Match.CountyName.Should().Be("Cluj");
    }
}
