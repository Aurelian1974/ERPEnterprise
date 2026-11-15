namespace Erp.Setup.Sql;

/// <summary>Schemele bazei in ordinea de dependenta a modulelor (8.5); folosita la bootstrap, roluri si ordinea migratiilor.</summary>
public static class ModuleOrder
{
    public static readonly IReadOnlyList<string> Schemas =
    [
        "core", "ids", "hangfire", "intg", "fin", "inv", "sales", "purch", "wms", "tms", "mfg", "hr", "pos", "cons", "crm", "rpt",
    ];
}
