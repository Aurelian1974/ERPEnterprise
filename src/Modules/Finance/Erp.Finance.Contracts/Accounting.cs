using Erp.Core.Contracts;

namespace Erp.Finance.Contracts.Accounting;

/// <summary>Sursa unei note contabile (C2 3.5): eveniment, manuala, reclasificare, inchidere, deschidere, storno.</summary>
public enum EntrySourceKind : byte
{
    Event = 1,
    Manual = 2,
    Reclassification = 3,
    Closing = 4,
    Opening = 5,
    Reversal = 6,
}

/// <summary>Validarea oricarei note: automata, manuala, storno (C2 5.1). Consumatori: Raportare, C4 (SAF-T incremental), C1.</summary>
public sealed record NotaContabilaValidata(long EntryId, string JournalCode, int Number, DateOnly AccountingDate, EntrySourceKind SourceKind,
    string? SourceDocumentType, long? SourceDocumentId, decimal TotalRon) : IntegrationEvent;
