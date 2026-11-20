using System.Globalization;
using System.Text;
using Erp.Core.Domain;

namespace Erp.Core.Application.Partners;

/// <summary>Un fisier generat de server (Excel), descarcat de interfata.</summary>
public sealed record FileDto(string FileName, string ContentType, byte[] Content)
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>O coloana a foii de parteneri: cheia tehnica, antetul din Excel si un exemplu pentru sablon.</summary>
public sealed record PartnerSheetColumn(string Key, string Header, bool Required, string Example);

/// <summary>Un rand citit din Excel: numarul randului din foaie si valorile, in ordinea coloanelor foii (implicit cea de parteneri).</summary>
public sealed record PartnerSheetRow(int RowNumber, IReadOnlyList<string?> Values, SheetDefinition? Sheet = null)
{
    public string? this[string key] => Values[(Sheet ?? PartnerSheet.Definition).IndexOf(key)];
}

/// <summary>O lista de valori permise (foaia „Liste”), legata de coloanele care o folosesc ca lista derulanta.</summary>
public sealed record SheetList(string Header, IReadOnlyList<string> Values, IReadOnlyList<string> ColumnKeys);

/// <summary>Foaia Excel a unei entitati (parteneri, articole): numele foii si coloanele, in ordine.</summary>
public sealed class SheetDefinition(string name, IReadOnlyList<PartnerSheetColumn> columns)
{
    public string Name { get; } = name;

    public IReadOnlyList<PartnerSheetColumn> Columns { get; } = columns;

    public int IndexOf(string key)
    {
        for (var i = 0; i < Columns.Count; i++)
        {
            if (Columns[i].Key == key)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(key), key, "Coloana nu există.");
    }

    public string Header(string key) => Columns[IndexOf(key)].Header;
}

/// <summary>Listele de valori puse in sablon (foaia „Liste”) si folosite la validarea listelor derulante din Excel.</summary>
public sealed record PartnerSheetLists(IReadOnlyList<string> Groups, IReadOnlyList<string> Counties, IReadOnlyList<string> Countries);

/// <summary>Citirea si scrierea foilor Excel de parteneri (ClosedXML, in Infrastructure).</summary>
public interface IPartnerSpreadsheet
{
    /// <summary>Foaia unei entitati oarecare, cu listele ei de valori; errors adauga coloana „Eroare”.</summary>
    byte[] Write(SheetDefinition sheet, IReadOnlyList<IReadOnlyList<string?>> rows, IReadOnlyList<SheetList> lists, IReadOnlyList<string>? errors = null);

    IReadOnlyList<PartnerSheetRow> Read(SheetDefinition sheet, byte[] content);

    /// <summary>Foaia „Parteneri” cu antetul coloanelor si randurile date; errorColumn adauga la final coloana „Eroare”.</summary>
    byte[] Write(IReadOnlyList<IReadOnlyList<string?>> rows, PartnerSheetLists lists, IReadOnlyList<string>? errors = null);

    /// <summary>Randurile nevide ale primei foi; ErpValidationException pe „file” daca nu e un fisier Excel cu antetul sablonului.</summary>
    IReadOnlyList<PartnerSheetRow> Read(byte[] content);
}

/// <summary>
/// Coloanele exportului si ale sablonului de import (aceleasi, ca un export sa poata fi corectat si reimportat; 02.01, 4.8). Doar
/// datele generale (decizia lui Aurelian, 20.11.2026): fara CNP, conturi bancare, contacte sau proprietati.
/// </summary>
public static class PartnerSheet
{
    public const string SheetName = "Parteneri";
    public const string ListsSheetName = "Liste";
    public const string ErrorHeader = "Eroare";

    public static readonly IReadOnlyList<PartnerSheetColumn> Columns =
    [
        new("Code", "Cod", false, "AGN"),
        new("Name", "Denumire", true, "Agro Nord Distribuție SRL"),
        new("PartnerType", "Tip", true, "Juridică RO"),
        new("IsCustomer", "Client", true, "Da"),
        new("IsSupplier", "Furnizor", true, "Nu"),
        new("FiscalCode", "CUI", false, "RO33445566"),
        new("TradeRegisterNo", "Nr. registrul comerțului", false, "J33/567/2012"),
        new("VatPayer", "Plătitor TVA", false, "Da"),
        new("VatOnCollection", "TVA la încasare", false, "Nu"),
        new("AnafInactive", "Inactiv ANAF", false, "Nu"),
        new("PartnerGroup", "Grupă (cod)", false, ""),
        new("Address", "Adresă", true, "Str. Gării nr. 21"),
        new("City", "Localitate", true, "Suceava"),
        new("County", "Județ", false, "SV"),
        new("Country", "Țară", false, "RO"),
        new("PostalCode", "Cod poștal", false, "720004"),
        new("Phone", "Telefon", false, "0230 123 456"),
        new("Email", "E-mail", false, "office@exemplu.ro"),
        new("IsActive", "Activ", false, "Da"),
    ];

    public static readonly IReadOnlyList<string> TypeNames = ["Juridică RO", "Juridică UE", "Juridică non-UE", "Persoană fizică"];

    public static readonly SheetDefinition Definition = new(SheetName, Columns);

    public static int IndexOf(string key) => Definition.IndexOf(key);

    public static string Header(string key) => Definition.Header(key);

    /// <summary>Listele foii de parteneri: tipurile, Da/Nu, grupele, judetele si tarile.</summary>
    public static IReadOnlyList<SheetList> Lists(PartnerSheetLists lists) =>
    [
        new(Header("PartnerType"), TypeNames, ["PartnerType"]),
        new("Da / Nu", ["Da", "Nu"], ["IsCustomer", "IsSupplier", "VatPayer", "VatOnCollection", "AnafInactive", "IsActive"]),
        new(Header("PartnerGroup"), lists.Groups, ["PartnerGroup"]),
        new(Header("County"), lists.Counties, ["County"]),
        new(Header("Country"), lists.Countries, ["Country"]),
    ];

    /// <summary>Textul fara diacritice, cu litere mici si fara spatii la capete (compararea valorilor din Excel).</summary>
    public static string Fold(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>Da/Nu din Excel (si 1/0, x, true/false); null = gol sau valoare nerecunoscuta (vezi <paramref name="recognized"/>).</summary>
    public static bool? YesNo(string? value, out bool recognized)
    {
        recognized = true;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        switch (Fold(value))
        {
            case "da" or "1" or "x" or "true" or "adevarat":
                return true;
            case "nu" or "0" or "false" or "fals":
                return false;
            default:
                recognized = false;
                return null;
        }
    }

    /// <summary>Tipul partenerului dupa nume (cu sau fara diacritice) sau cifra 1–4.</summary>
    public static PartnerType? Type(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Fold(value) switch
        {
            "1" or "juridica ro" or "juridica romania" or "pj ro" => PartnerType.LegalRomania,
            "2" or "juridica ue" or "pj ue" => PartnerType.LegalEu,
            "3" or "juridica non-ue" or "juridica non ue" or "pj non-ue" => PartnerType.LegalNonEu,
            "4" or "persoana fizica" or "pf" => PartnerType.Individual,
            _ => null,
        };
    }

    public static string TypeName(byte type) => type is >= 1 and <= 4 ? TypeNames[type - 1] : type.ToString(CultureInfo.InvariantCulture);

    public static string YesNoText(bool value) => value ? "Da" : "Nu";
}
