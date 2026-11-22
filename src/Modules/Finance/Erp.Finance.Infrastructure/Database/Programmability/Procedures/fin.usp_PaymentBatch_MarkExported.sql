CREATE OR ALTER PROCEDURE fin.usp_PaymentBatch_MarkExported
    @CompanyId       int,
    @Id              int,
    @RowVersion      binary(8),
    @Format          tinyint,
    @FileName        nvarchar(260),
    @FileSha256      char(64),
    @FileContent     varbinary(max),
    @ReExportReason  nvarchar(500) = NULL,
    @RequireApproval bit,
    @UserId          int
AS
BEGIN
    -- Exportul unui lot (02.02, F2 3.6 si 4.2 pasul 4), in tranzactia apelantului: toate OP-urile validate, nestornate, nerespinse si, cu
    -- patru ochi activ, aprobate (51222). Un lot deja exportat se reexporta doar cu motiv (51218; permisiunea o verifica handler-ul);
    -- fisierul nou are aceleasi EndToEndId. Un lot anulat nu se exporta (51222).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @BatchCompanyId int, @Version binary(8);
    SELECT @Status = Status, @BatchCompanyId = CompanyId, @Version = RowVersion FROM fin.PaymentBatch WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @Status IS NULL OR @BatchCompanyId <> @CompanyId
        THROW 51226, N'Lotul de plăți nu există.', 1;
    IF @Version <> @RowVersion
        THROW 51202, N'Lotul de plăți a fost modificat de alt utilizator; reîncărcați datele.', 1;
    IF @Status = 3
        THROW 51222, N'Lotul este anulat.', 1;
    IF @Status = 2 AND @ReExportReason IS NULL
        THROW 51218, N'Reexportul unui lot cere un motiv.', 1;
    IF NOT EXISTS (SELECT 1 FROM fin.PaymentOrder WHERE PaymentBatchId = @Id)
        THROW 51222, N'Lotul nu are ordine de plată.', 1;
    IF EXISTS (SELECT 1 FROM fin.PaymentOrder WITH (UPDLOCK)
               WHERE PaymentBatchId = @Id
                 AND (Status <> 2 OR ReversalOfId IS NOT NULL OR ConfirmationStatus = 3 OR (@RequireApproval = 1 AND Origin <> 2 AND ApprovedAt IS NULL)))
        THROW 51222, N'Lotul are ordine de plată nevalidate, stornate sau, cu patru ochi activ, neaprobate.', 1;

    UPDATE fin.PaymentBatch
    SET Status = 2, Format = @Format, ExportCount = ExportCount + 1, ExportedAt = SYSUTCDATETIME(), ExportedBy = @UserId, FileName = @FileName,
        FileSha256 = @FileSha256, FileContent = @FileContent, LastReExportReason = CASE WHEN @Status = 2 THEN @ReExportReason ELSE LastReExportReason END,
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id;

    SELECT RowVersion FROM fin.PaymentBatch WHERE Id = @Id;
END;
