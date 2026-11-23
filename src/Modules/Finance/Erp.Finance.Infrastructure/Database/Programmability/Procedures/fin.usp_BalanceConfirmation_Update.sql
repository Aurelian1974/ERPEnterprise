CREATE OR ALTER PROCEDURE fin.usp_BalanceConfirmation_Update
    @Id                    int,
    @CompanyId             int,
    @GeneratedAttachmentId int = NULL,
    @SentTo                nvarchar(254) = NULL,
    @EmailMessageId        int = NULL,
    @ResponseStatus        tinyint = NULL,
    @ResponseDate          date = NULL,
    @PartnerBalance        decimal(19,2) = NULL,
    @ResponseNote          nvarchar(1000) = NULL,
    @ResponseAttachmentId  int = NULL,
    @IsResolved            bit = NULL,
    @RowVersion            binary(8) = NULL,
    @UserId                int
AS
BEGIN
    -- Actualizeaza o confirmare (F3 3.9, 4.6): PDF-ul generat, trimiterea (SentAt, SentTo, mesajul, starea Trimisa) sau raspunsul (starea,
    -- data, soldul partenerului si diferenta fata de al nostru, nota, fisierul primit, rezolvarea). Parametrii NULL nu schimba nimic. Versiune
    -- data si diferita: 51417.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @ConfirmationCompanyId int, @Version binary(8), @Our decimal(19,2);
    SELECT @ConfirmationCompanyId = CompanyId, @Version = RowVersion, @Our = OurBalance FROM fin.BalanceConfirmation WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id;
    IF @ConfirmationCompanyId IS NULL OR @ConfirmationCompanyId <> @CompanyId
        THROW 51421, N'Confirmarea de sold nu există.', 1;
    IF @RowVersion IS NOT NULL AND @Version <> @RowVersion
        THROW 51417, N'Confirmarea a fost modificată de alt utilizator; reîncărcați datele.', 1;

    UPDATE fin.BalanceConfirmation
    SET GeneratedAttachmentId = ISNULL(@GeneratedAttachmentId, GeneratedAttachmentId),
        SentTo = ISNULL(@SentTo, SentTo), SentAt = CASE WHEN @SentTo IS NOT NULL THEN SYSUTCDATETIME() ELSE SentAt END,
        EmailMessageId = ISNULL(@EmailMessageId, EmailMessageId),
        ResponseStatus = CASE WHEN @ResponseStatus IS NOT NULL THEN @ResponseStatus WHEN @SentTo IS NOT NULL AND ResponseStatus = 0 THEN 1 ELSE ResponseStatus END,
        ResponseDate = CASE WHEN @ResponseStatus IS NOT NULL THEN @ResponseDate ELSE ResponseDate END,
        PartnerBalance = CASE WHEN @ResponseStatus IS NOT NULL THEN @PartnerBalance ELSE PartnerBalance END,
        Difference = CASE WHEN @ResponseStatus IS NOT NULL THEN CASE WHEN @PartnerBalance IS NULL THEN NULL ELSE @PartnerBalance - @Our END ELSE Difference END,
        ResponseNote = CASE WHEN @ResponseStatus IS NOT NULL THEN @ResponseNote ELSE ResponseNote END,
        ResponseAttachmentId = ISNULL(@ResponseAttachmentId, ResponseAttachmentId),
        IsResolved = ISNULL(@IsResolved, IsResolved),
        ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
    OUTPUT inserted.RowVersion
    WHERE Id = @Id;
END;
