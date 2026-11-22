using Erp.Core.Contracts;
using Erp.Finance.Contracts.Bank;
using Erp.Finance.Domain;
using Erp.Finance.Domain.Bank;
using FluentValidation;

namespace Erp.Finance.Application.Bank;

/// <summary>Cititorul unui format de extras (F2 6: Camt053Parser, Mt940Parser, CsvStatementParser), in Infrastructure/Bank/Formats.</summary>
public interface IStatementParser
{
    StatementFormat Format { get; }

    /// <summary>Extrasele din fisier; StatementFormatException cand fisierul nu poate fi citit (cu randul si motivul).</summary>
    IReadOnlyList<ParsedStatement> Parse(byte[] content, string? mapping);
}

/// <summary>Contul de trezorerie in care se importa, cu profilul lui bancar.</summary>
public sealed record BankImportAccount(int Id, string Code, string CurrencyCode, string? Iban, StatementFormat? StatementFormat, string? CsvMapping);

/// <summary>Un extras de salvat: antetul si liniile cu referintele finale.</summary>
public sealed record BankStatementToImport(int TreasuryAccountId, int? BankStatementFileId, string? StatementNo, DateOnly StatementDate, DateOnly PeriodFrom,
    DateOnly PeriodTo, string CurrencyCode, decimal OpeningBalance, decimal ClosingBalance, StatementSource Source, IReadOnlyList<ParsedStatementLine> Lines,
    IReadOnlyList<string> References);

public sealed record BankStatementImported(int BankStatementId, int LineCount, int SkippedCount, ContinuityStatus ContinuityStatus);

public sealed record ImportedStatementDto(int BankStatementId, int TreasuryAccountId, string? StatementNo, DateOnly StatementDate, int LineCount,
    int SkippedCount, ContinuityStatus ContinuityStatus);

public sealed record BankImportResultDto(IReadOnlyList<ImportedStatementDto> Statements);

/// <summary>Un rand al listei de extrase (macheta f2-01).</summary>
public sealed record BankStatementItemDto(int Id, int TreasuryAccountId, string AccountCode, string AccountName, string? StatementNo, DateOnly StatementDate,
    DateOnly PeriodFrom, DateOnly PeriodTo, string CurrencyCode, string OpeningBalance, string ClosingBalance, StatementSource Source,
    BankStatementStatus Status, ContinuityStatus ContinuityStatus, int LineCount, int ValidatedCount, byte[] RowVersion);

public sealed record BankStatementLineDto(long Id, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate, string Amount, string? CounterpartyName,
    string? CounterpartyIban, string? CounterpartyBic, string? RemittanceInfo, string? EndToEndId, string BankReference, string? BankTransactionCode,
    string? ForeignAmount, string? ForeignCurrency, BankLineMatchStatus MatchStatus, int? PartnerId, string? PartnerName, byte[] RowVersion);

public sealed record BankStatementDetailDto(BankStatementItemDto Statement, string? FileName, string? GapReason, DateTime? GapAcceptedAt,
    string? GapAcceptedByName, string? PreviousClosingBalance, IReadOnlyList<BankStatementLineDto> Lines);

public sealed record BankStatementRow(int Id, int TreasuryAccountId, string AccountCode, string AccountName, string? StatementNo, DateOnly StatementDate,
    DateOnly PeriodFrom, DateOnly PeriodTo, string CurrencyCode, decimal OpeningBalance, decimal ClosingBalance, StatementSource Source,
    BankStatementStatus Status, ContinuityStatus ContinuityStatus, int LineCount, int ValidatedCount, byte[] RowVersion);

public sealed record BankStatementLineRow(long Id, int LineNumber, DateOnly BookingDate, DateOnly? ValueDate, decimal Amount, string? CounterpartyName,
    string? CounterpartyIban, string? CounterpartyBic, string? RemittanceInfo, string? EndToEndId, string BankReference, string? BankTransactionCode,
    decimal? ForeignAmount, string? ForeignCurrency, BankLineMatchStatus MatchStatus, int? PartnerId, string? PartnerName, byte[] RowVersion);

public sealed record BankStatementDetailRow(BankStatementRow Statement, string? FileName, string? GapReason, DateTime? GapAcceptedAt, string? GapAcceptedByName,
    decimal? PreviousClosingBalance, IReadOnlyList<BankStatementLineRow> Lines);

/// <summary>Accesul la date pentru extrase (fin.usp_BankStatement_*, NET-06).</summary>
public interface IBankStatementStore
{
    Task<BankImportAccount?> GetImportAccountAsync(int companyId, int treasuryAccountId, CancellationToken ct);

    Task<BankImportAccount?> FindImportAccountByIbanAsync(int companyId, string iban, CancellationToken ct);

    /// <summary>Soldul final al ultimului extras de dinaintea zilei (pentru CSV-urile fara sold).</summary>
    Task<decimal?> GetPreviousClosingAsync(int treasuryAccountId, DateOnly before, CancellationToken ct);

    /// <summary>Fisierul original (51206 daca acelasi hash exista deja pe cont).</summary>
    Task<int> AddFileAsync(int companyId, int treasuryAccountId, string fileName, string sha256, byte[] content, int userId, CancellationToken ct);

    Task<BankStatementImported> ImportAsync(int companyId, BankStatementToImport statement, int userId, CancellationToken ct);

    Task<byte[]> AcceptGapAsync(int companyId, int bankStatementId, string reason, byte[] rowVersion, int userId, CancellationToken ct);

    Task<IReadOnlyList<BankStatementRow>> ListAsync(int companyId, int? treasuryAccountId, DateOnly? from, DateOnly? to, CancellationToken ct);

    Task<BankStatementDetailRow?> GetAsync(int companyId, int bankStatementId, CancellationToken ct);
}

internal static class BankErrors
{
    public static ErpException FileInvalid(string message) => new(ErpErrorKind.Validation, "FIN_BANK_FILE_INVALID", message);

    public static ErpException AccountNotFound(string message) => new(ErpErrorKind.Conflict, "FIN_BANK_ACCOUNT_NOT_FOUND", message);

    public static BankStatementItemDto Item(BankStatementRow r) => new(r.Id, r.TreasuryAccountId, r.AccountCode, r.AccountName, r.StatementNo, r.StatementDate,
        r.PeriodFrom, r.PeriodTo, r.CurrencyCode, Money.Format(r.OpeningBalance), Money.Format(r.ClosingBalance), r.Source, r.Status, r.ContinuityStatus,
        r.LineCount, r.ValidatedCount, r.RowVersion);
}

/// <summary>
/// Importul unui fisier de extras (F2 4.1): contul ales (sau cel cu IBAN-ul din fisier, doar din firma curenta), formatul din profilul
/// contului (sau recunoscut dupa continut), fisierul nesigur citit cu limite (SEC-01, SEC-05), hash-ul unic pe cont (51206), apoi
/// fiecare extras din fisier: moneda (51209), soldurile fata de linii (51208), continuitatea cu extrasul anterior, liniile existente
/// sarite dupa BankReference. Totul intr-o tranzactie; ExtrasBancarImportat in outbox pentru fiecare extras. Continutul vine Base64.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementImport)]
[Audited("Fin.BankStatement.Import")]
public sealed record ImportBankStatement(int? TreasuryAccountId, string FileName, string ContentBase64) : ICommand<BankImportResultDto>;

internal sealed class ImportBankStatementValidator : AbstractValidator<ImportBankStatement>
{
    public ImportBankStatementValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithMessage("Lipsește numele fișierului.").MaximumLength(260);
        RuleFor(x => x.ContentBase64).NotEmpty().WithMessage("Fișierul este gol.")
            .Must(c => c.Length <= (BankStatementRules.MaxFileBytes + 2) / 3 * 4).WithMessage("Fișierul depășește 20 MB.");
    }
}

internal sealed class ImportBankStatementHandler(IBankStatementStore store, IEnumerable<IStatementParser> parsers, IOutbox outbox, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<ImportBankStatement, BankImportResultDto>
{
    public async Task<BankImportResultDto> Handle(ImportBankStatement request, CancellationToken cancellationToken)
    {
        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw BankErrors.FileInvalid("Conținutul fișierului nu a fost transmis corect.");
        }

        if (content.Length == 0 || content.Length > BankStatementRules.MaxFileBytes)
        {
            throw BankErrors.FileInvalid("Fișierul este gol sau depășește 20 MB.");
        }

        var chosen = request.TreasuryAccountId is { } id
            ? await store.GetImportAccountAsync(context.CompanyId, id, cancellationToken).ConfigureAwait(false)
              ?? throw ErpException.NotFound(FinanceErrorCodes.TreasuryAccountNotFound, "Contul bancar nu există în firma curentă.")
            : null;
        var format = chosen?.StatementFormat ?? BankStatementRules.Detect(content);
        if (format == StatementFormat.None)
        {
            throw BankErrors.FileInvalid(chosen?.StatementFormat == StatementFormat.None
                ? "Profilul bancar al contului nu are import de extrase."
                : "Formatul fișierului nu a fost recunoscut; alegeți contul (cu profilul lui bancar).");
        }

        if (format == StatementFormat.Csv && chosen is null)
        {
            throw BankErrors.FileInvalid("Un extras CSV se importă doar în contul ales, după profilul lui.");
        }

        var parser = parsers.FirstOrDefault(p => p.Format == format) ?? throw BankErrors.FileInvalid("Formatul nu este suportat.");
        IReadOnlyList<ParsedStatement> statements;
        try
        {
            statements = parser.Parse(content, chosen?.CsvMapping);
        }
        catch (StatementFormatException ex)
        {
            throw BankErrors.FileInvalid(ex.Message);
        }

        var sha = BankStatementRules.Sha256Hex(content);
        var files = new Dictionary<int, int>();
        var imported = new List<ImportedStatementDto>();
        foreach (var statement in statements)
        {
            var account = await ResolveAccountAsync(chosen, statement, cancellationToken).ConfigureAwait(false);
            if (!files.TryGetValue(account.Id, out var fileId))
            {
                fileId = await store.AddFileAsync(context.CompanyId, account.Id, request.FileName.Trim(), sha, content, context.UserId, cancellationToken)
                    .ConfigureAwait(false);
                files[account.Id] = fileId;
            }

            var opening = statement.OpeningBalance
                ?? await store.GetPreviousClosingAsync(account.Id, statement.PeriodFrom, cancellationToken).ConfigureAwait(false) ?? 0m;
            var closing = statement.ClosingBalance ?? opening + statement.Lines.Sum(l => l.Amount);
            var toImport = new BankStatementToImport(account.Id, fileId, statement.StatementNo, statement.StatementDate, statement.PeriodFrom, statement.PeriodTo,
                (statement.CurrencyCode ?? account.CurrencyCode).ToUpperInvariant(), opening, closing, BankStatementRules.SourceOf(format), statement.Lines,
                BankStatementRules.References(account.Id, statement.Lines));
            var result = await store.ImportAsync(context.CompanyId, toImport, context.UserId, cancellationToken).ConfigureAwait(false);
            await outbox.EnqueueAsync(new ExtrasBancarImportat(result.BankStatementId, account.Id, statement.StatementDate, result.LineCount, opening, closing,
                (byte)result.ContinuityStatus) { CompanyId = context.CompanyId }, cancellationToken).ConfigureAwait(false);
            imported.Add(new ImportedStatementDto(result.BankStatementId, account.Id, statement.StatementNo, statement.StatementDate, result.LineCount,
                result.SkippedCount, result.ContinuityStatus));
        }

        audit.Describe("Fin.BankStatement", imported[0].BankStatementId, new
        {
            FileName = request.FileName.Trim(), FileSha256 = sha, Format = format.ToString(),
            Statements = imported.Select(i => new { i.BankStatementId, i.TreasuryAccountId, i.StatementDate, i.LineCount, i.SkippedCount, i.ContinuityStatus }),
        });
        return new BankImportResultDto(imported);
    }

    /// <summary>Contul extrasului: cel ales (IBAN-ul din fisier, daca exista, trebuie sa fie al lui) sau cel cu IBAN-ul din fisier (51207).</summary>
    private async Task<BankImportAccount> ResolveAccountAsync(BankImportAccount? chosen, ParsedStatement statement, CancellationToken ct)
    {
        if (chosen is not null)
        {
            return statement.AccountIban is null || chosen.Iban is null || string.Equals(statement.AccountIban, chosen.Iban, StringComparison.Ordinal)
                ? chosen
                : throw BankErrors.AccountNotFound($"Extrasul este pentru IBAN-ul {statement.AccountIban}, nu pentru contul {chosen.Code}.");
        }

        return statement.AccountIban is null
            ? throw BankErrors.FileInvalid("Fișierul nu conține IBAN-ul contului; alegeți contul.")
            : await store.FindImportAccountByIbanAsync(context.CompanyId, statement.AccountIban, ct).ConfigureAwait(false)
              ?? throw BankErrors.AccountNotFound($"IBAN-ul {statement.AccountIban} nu este un cont de trezorerie al firmei curente.");
    }
}

/// <summary>
/// Accepta golul de continuitate al unui extras (F2 3.3): cu motiv scris, permisiune si audit; liniile extrasului se pot valida apoi.
/// </summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankStatementAcceptGap)]
[Audited("Fin.BankStatement.AcceptGap")]
public sealed record AcceptStatementGap(int BankStatementId, string Reason, byte[] RowVersion) : ICommand<byte[]>;

internal sealed class AcceptStatementGapValidator : AbstractValidator<AcceptStatementGap>
{
    public AcceptStatementGapValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Acceptarea golului cere un motiv scris.").MaximumLength(500);
        RuleFor(x => x.RowVersion).NotEmpty().WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class AcceptStatementGapHandler(IBankStatementStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<AcceptStatementGap, byte[]>
{
    public async Task<byte[]> Handle(AcceptStatementGap request, CancellationToken cancellationToken)
    {
        var version = await store.AcceptGapAsync(context.CompanyId, request.BankStatementId, request.Reason.Trim(), request.RowVersion, context.UserId,
            cancellationToken).ConfigureAwait(false);
        audit.Describe("Fin.BankStatement", request.BankStatementId, new { Reason = request.Reason.Trim() });
        return version;
    }
}

/// <summary>Lista extraselor firmei (macheta f2-01), filtrata pe cont si interval; cele mai noi primele.</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetBankStatements(int? TreasuryAccountId = null, DateOnly? From = null, DateOnly? To = null) : IQuery<IReadOnlyList<BankStatementItemDto>>;

internal sealed class GetBankStatementsHandler(IBankStatementStore store, ICurrentContext context)
    : IQueryHandler<GetBankStatements, IReadOnlyList<BankStatementItemDto>>
{
    public async Task<IReadOnlyList<BankStatementItemDto>> Handle(GetBankStatements request, CancellationToken cancellationToken) =>
        (await store.ListAsync(context.CompanyId, request.TreasuryAccountId, request.From, request.To, cancellationToken).ConfigureAwait(false))
        .Select(BankErrors.Item).ToList();
}

/// <summary>Un extras cu liniile lui (macheta f2-02).</summary>
[Module(FinanceModule.Code)]
[RequiresPermission(FinancePermissions.BankView)]
public sealed record GetBankStatement(int Id) : IQuery<BankStatementDetailDto>;

internal sealed class GetBankStatementHandler(IBankStatementStore store, ICurrentContext context) : IQueryHandler<GetBankStatement, BankStatementDetailDto>
{
    public async Task<BankStatementDetailDto> Handle(GetBankStatement request, CancellationToken cancellationToken)
    {
        var row = await store.GetAsync(context.CompanyId, request.Id, cancellationToken).ConfigureAwait(false)
            ?? throw ErpException.NotFound("FIN_BANK_STATEMENT_NOT_FOUND", "Extrasul nu există în firma curentă.");
        var lines = row.Lines.Select(l => new BankStatementLineDto(l.Id, l.LineNumber, l.BookingDate, l.ValueDate, Money.Format(l.Amount), l.CounterpartyName,
            l.CounterpartyIban, l.CounterpartyBic, l.RemittanceInfo, l.EndToEndId, l.BankReference, l.BankTransactionCode,
            l.ForeignAmount is { } f ? Money.Format(f) : null, l.ForeignCurrency, l.MatchStatus, l.PartnerId, l.PartnerName, l.RowVersion)).ToList();
        return new BankStatementDetailDto(BankErrors.Item(row.Statement), row.FileName, row.GapReason, row.GapAcceptedAt, row.GapAcceptedByName,
            row.PreviousClosingBalance is { } p ? Money.Format(p) : null, lines);
    }
}
