using Erp.Core.Infrastructure.Data;

namespace Erp.Core.Tests;

/// <summary>Tipurile permise si verificarea continutului atasamentelor (02.01, SEC-05).</summary>
public sealed class AttachmentRulesTests
{
    [Theory]
    [InlineData("PV semnat.PDF", "PV semnat.PDF", "application/pdf")]
    [InlineData(@"C:\scan\pv.jpg", "pv.jpg", "image/jpeg")]
    [InlineData("../../etc/factura.xml", "factura.xml", "application/xml")]
    public void Allowed_types_keep_only_the_file_name_and_take_the_type_from_the_extension(string input, string name, string type) =>
        Assert.Equal((name, type), AttachmentRules.Normalize(input));

    [Theory]
    [InlineData("script.exe")]
    [InlineData("pagina.html")]
    [InlineData("fara-extensie")]
    [InlineData("")]
    public void Other_types_are_refused(string input) => Assert.Null(AttachmentRules.Normalize(input));

    [Fact]
    public void The_content_must_match_the_type()
    {
        Assert.True(AttachmentRules.Matches("application/pdf", "%PDF-1.7"u8));
        Assert.False(AttachmentRules.Matches("application/pdf", new byte[] { 0x4D, 0x5A, 0x90, 0 }));
        Assert.True(AttachmentRules.Matches("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }));
        Assert.True(AttachmentRules.Matches("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        Assert.True(AttachmentRules.Matches("application/xml", new byte[] { 0xEF, 0xBB, 0xBF, 0x20, 0x3C, 0x3F }));
        Assert.False(AttachmentRules.Matches("application/xml", "hello"u8));
    }
}
