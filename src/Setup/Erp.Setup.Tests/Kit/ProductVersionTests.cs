using Erp.Setup.Core.Kit;

namespace Erp.Setup.Tests.Kit;

public sealed class ProductVersionTests
{
    [Fact]
    public void Compares_five_components_numerically()
    {
        Assert.True(ProductVersion.Parse("2026.11.8.2.10") > ProductVersion.Parse("2026.11.8.2.9"));
        Assert.True(ProductVersion.Parse("2026.11.8.3.0") > ProductVersion.Parse("2026.11.8.2.99"));
    }

    [Fact]
    public void Shorter_forms_are_padded_with_zeros()
    {
        Assert.Equal(ProductVersion.Parse("2026.4.2"), ProductVersion.Parse("2026.4.2.0.0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("2026.1.1.1.1.1")]
    [InlineData("2026.a.1")]
    public void Rejects_invalid_versions(string text) => Assert.False(ProductVersion.TryParse(text, out _));
}
