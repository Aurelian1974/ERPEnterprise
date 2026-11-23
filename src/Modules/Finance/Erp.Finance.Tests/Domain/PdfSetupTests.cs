using Erp.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Erp.Finance.Tests.Domain;

/// <summary>QuestPDF instalat si pornit: licenta din configurare (implicit Community) si un document generat efectiv (bibliotecile native).</summary>
public sealed class PdfSetupTests
{
    [Fact]
    public void The_license_comes_from_the_configuration_and_a_document_is_generated()
    {
        Assert.Equal(LicenseType.Community, PdfSetup.Configure(null));
        Assert.Equal(LicenseType.Professional, PdfSetup.Configure("professional"));
        Assert.Throws<InvalidOperationException>(() => PdfSetup.Configure("gratis"));
        PdfSetup.Configure("Community");

        var pdf = Document.Create(document => document.Page(page => page.Content().Text("Registrul de casă – test, diacritice: ăâîșț"))).GeneratePdf();

        Assert.True(pdf.Length > 500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
