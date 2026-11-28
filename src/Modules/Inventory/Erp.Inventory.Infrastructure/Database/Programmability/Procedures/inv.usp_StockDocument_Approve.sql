CREATE OR ALTER PROCEDURE inv.usp_StockDocument_Approve
    @CompanyId  int,
    @Id         int,
    @Approve    bit,
    @Note       nvarchar(500) = NULL,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Aprobarea sau respingerea unei descarcari peste prag (02.05, S2 3.6): doar o ciorna care asteapta aprobarea (54112), nu de catre autor
    -- (54113, patru ochi). Respingerea cere motiv (54112). Coduri: 54001, 54117.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint, @Approval tinyint, @Author int;
    SELECT @Status = Status, @Approval = ApprovalStatus, @Author = CreatedBy FROM inv.StockDocument WITH (UPDLOCK, ROWLOCK)
    WHERE Id = @Id AND CompanyId = @CompanyId;
    IF @Status IS NULL
        THROW 54001, N'Documentul nu există în firma curentă.', 1;
    IF @Status <> 1 OR @Approval <> 1
        THROW 54112, N'Documentul nu așteaptă aprobare.', 1;
    IF @Author = @UserId
        THROW 54113, N'Autorul descărcării nu o poate aproba.', 1;
    IF @Approve = 0 AND NULLIF(LTRIM(RTRIM(@Note)), N'') IS NULL
        THROW 54112, N'Respingerea cere motivul.', 1;

    UPDATE inv.StockDocument
    SET ApprovalStatus = CASE WHEN @Approve = 1 THEN 2 ELSE 3 END, ApprovalNote = @Note, ApprovedBy = @UserId, ApprovedAt = SYSUTCDATETIME(),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54117, N'Documentul a fost modificat de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion FROM inv.StockDocument WHERE Id = @Id;
END;
