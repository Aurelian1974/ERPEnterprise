using ClosedXML.Excel;
using Erp.Core.Application.Partners;
using Erp.Core.Contracts;

namespace Erp.Core.Infrastructure.Partners;

/// <summary>Foile Excel de parteneri cu ClosedXML (07 Stack tehnologic): exportul, sablonul de import si randurile cu erori.</summary>
public sealed class PartnerSpreadsheet : IPartnerSpreadsheet
{
    /// <summary>Un import are cel mult atatea randuri (A18 cere 10.000; limita lasa loc si protejeaza serverul).</summary>
    public const int MaxRows = 50_000;

    public byte[] Write(IReadOnlyList<IReadOnlyList<string?>> rows, PartnerSheetLists lists, IReadOnlyList<string>? errors = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(PartnerSheet.SheetName);
        var columns = PartnerSheet.Columns;
        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Required ? columns[c].Header + " *" : columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F2");
        }

        if (errors is not null)
        {
            var header = sheet.Cell(1, columns.Count + 1);
            header.Value = PartnerSheet.ErrorHeader;
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml("#F8D7DA");
        }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns.Count; c++)
            {
                // Totul ca text: codurile, CUI-urile si codurile postale nu-si pierd zerourile, nici nu devin numere.
                sheet.Cell(r + 2, c + 1).SetValue(rows[r][c] ?? string.Empty);
            }

            if (errors is not null)
            {
                sheet.Cell(r + 2, columns.Count + 1).SetValue(errors[r]);
            }
        }

        sheet.Columns(1, columns.Count).Style.NumberFormat.Format = "@";
        AddLists(workbook, sheet, lists);
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(rows.Count + 1, 200));
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyList<PartnerSheetRow> Read(byte[] content)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(content));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw FileError("Fișierul nu este un document Excel (.xlsx) valid. Folosiți șablonul descărcat din pagină.");
        }

        using (workbook)
        {
            var sheet = workbook.Worksheets.FirstOrDefault(w => w.Name == PartnerSheet.SheetName) ?? workbook.Worksheet(1);
            var map = HeaderMap(sheet);
            var missing = PartnerSheet.Columns.Where(c => c.Required && !map.ContainsKey(c.Key)).Select(c => c.Header).ToList();
            if (missing.Count > 0)
            {
                throw FileError($"Lipsesc coloanele: {string.Join(", ", missing)}. Folosiți șablonul descărcat din pagină.");
            }

            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
            if (last - 1 > MaxRows)
            {
                throw FileError($"Fișierul are peste {MaxRows:N0} de rânduri; împărțiți-l în mai multe importuri.");
            }

            var rows = new List<PartnerSheetRow>();
            for (var r = 2; r <= last; r++)
            {
                var row = sheet.Row(r);
                var values = PartnerSheet.Columns.Select(c => map.TryGetValue(c.Key, out var index) ? Text(row.Cell(index)) : null).ToArray();
                if (values.All(v => v is null))
                {
                    continue;
                }

                rows.Add(new PartnerSheetRow(r, values));
            }

            return rows;
        }
    }

    private static Dictionary<string, int> HeaderMap(IXLWorksheet sheet)
    {
        var byHeader = PartnerSheet.Columns.ToDictionary(c => PartnerSheet.Fold(c.Header), c => c.Key);
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in sheet.Row(1).CellsUsed())
        {
            var header = PartnerSheet.Fold(cell.GetString().Replace("*", string.Empty, StringComparison.Ordinal));
            if (byHeader.TryGetValue(header, out var key))
            {
                map.TryAdd(key, cell.Address.ColumnNumber);
            }
        }

        return map;
    }

    private static string? Text(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return null;
        }

        // O celula numerica (de exemplu CUI-ul tastat ca numar) se citeste fara separatoare si zecimale.
        var text = cell.DataType == XLDataType.Number
            ? cell.GetDouble().ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture)
            : cell.GetFormattedString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // Foaia „Liste” cu valorile permise si listele derulante pe coloanele cu valori fixe.
    private static void AddLists(XLWorkbook workbook, IXLWorksheet sheet, PartnerSheetLists lists)
    {
        var listSheet = workbook.Worksheets.Add(PartnerSheet.ListsSheetName);
        var data = new (string Header, IReadOnlyList<string> Values)[]
        {
            (PartnerSheet.Header("PartnerType"), PartnerSheet.TypeNames),
            ("Da / Nu", ["Da", "Nu"]),
            (PartnerSheet.Header("PartnerGroup"), lists.Groups),
            (PartnerSheet.Header("County"), lists.Counties),
            (PartnerSheet.Header("Country"), lists.Countries),
        };
        for (var c = 0; c < data.Length; c++)
        {
            listSheet.Cell(1, c + 1).Value = data[c].Header;
            listSheet.Cell(1, c + 1).Style.Font.Bold = true;
            for (var r = 0; r < data[c].Values.Count; r++)
            {
                listSheet.Cell(r + 2, c + 1).SetValue(data[c].Values[r]);
            }
        }

        listSheet.Columns().AdjustToContents();
        void Validate(string key, int listColumn, int count)
        {
            if (count == 0)
            {
                return;
            }

            var column = PartnerSheet.IndexOf(key) + 1;
            var letter = XLHelper.GetColumnLetterFromNumber(listColumn);
            var validation = sheet.Range(2, column, MaxRows + 1, column).CreateDataValidation();
            validation.List($"={PartnerSheet.ListsSheetName}!${letter}$2:${letter}${count + 1}", true);
            validation.IgnoreBlanks = true;
            // Valorile in alta forma (de exemplu cu diacritice lipsa) sunt acceptate de import; Excel doar avertizeaza.
            validation.ErrorStyle = XLErrorStyle.Warning;
        }

        Validate("PartnerType", 1, PartnerSheet.TypeNames.Count);
        foreach (var key in new[] { "IsCustomer", "IsSupplier", "VatPayer", "VatOnCollection", "AnafInactive", "IsActive" })
        {
            Validate(key, 2, 2);
        }

        Validate("PartnerGroup", 3, lists.Groups.Count);
        Validate("County", 4, lists.Counties.Count);
        Validate("Country", 5, lists.Countries.Count);
    }

    private static ErpValidationException FileError(string message) => new(new Dictionary<string, string[]> { ["file"] = [message] });
}
