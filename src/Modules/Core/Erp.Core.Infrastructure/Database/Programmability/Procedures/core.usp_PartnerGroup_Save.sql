CREATE OR ALTER PROCEDURE core.usp_PartnerGroup_Save
    @Id         int = NULL,
    @Code       nvarchar(20),
    @Name       nvarchar(100),
    @SortOrder  int,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Grupa de parteneri (02.01, 3.2): cod unic fara diferenta de majuscule; o grupa folosita de parteneri activi nu se dezactiveaza.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM core.PartnerGroup WITH (UPDLOCK, HOLDLOCK) WHERE CodeKey = UPPER(@Code) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Codul grupei există deja.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.PartnerGroup (Code, Name, SortOrder, IsActive, CreatedBy)
        VALUES (@Code, @Name, @SortOrder, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.Partner WHERE PartnerGroupId = @Id AND IsActive = 1)
            THROW 50010, N'Grupa are parteneri activi; mutați-i întâi în altă grupă.', 1;

        UPDATE core.PartnerGroup
        SET Code = @Code, Name = @Name, SortOrder = @SortOrder, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.PartnerGroup WHERE Id = @Id)
                THROW 50027, N'Grupa de parteneri nu există.', 1;
            THROW 50006, N'Grupa a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;
    END;

    SELECT Id, RowVersion FROM core.PartnerGroup WHERE Id = @Id;
END;
