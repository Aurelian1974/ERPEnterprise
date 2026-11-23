CREATE OR ALTER PROCEDURE fin.usp_Compensation_SetSigned
    @Id           int,
    @CompanyId    int,
    @AttachmentId int,
    @UserId       int
AS
BEGIN
    -- Ataseaza PV-ul semnat, scanat (F3 3.4, 4.4 pasul 2), la un PV ciorna sau validat; stornat: 51411.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @HeaderCompanyId int, @Status tinyint;
    SELECT @HeaderCompanyId = CompanyId, @Status = Status FROM fin.Compensation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @HeaderCompanyId IS NULL OR @HeaderCompanyId <> @CompanyId
        THROW 51021, N'Procesul-verbal nu există.', 1;
    IF @Status = 3
        THROW 51411, N'Procesul-verbal este stornat.', 1;

    UPDATE fin.Compensation SET SignedAttachmentId = @AttachmentId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.RowVersion
    WHERE Id = @Id;
END;
