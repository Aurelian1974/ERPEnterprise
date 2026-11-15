CREATE OR ALTER PROCEDURE core.usp_AuditEntry_Insert
    @OccurredAt    datetime2(3),
    @UserId        int,
    @CompanyId     int,
    @Action        nvarchar(100),
    @EntityType    nvarchar(100),
    @EntityId      bigint,
    @Changes       nvarchar(max),
    @IpAddress     varchar(45),
    @CorrelationId uniqueidentifier
AS
BEGIN
    -- P5, 4.1 pasul 8: jurnalul de audit, scris in aceeasi tranzactie cu modificarea (SP-03). Doar adaugare (SP-21).
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    INSERT INTO core.AuditEntry (OccurredAt, UserId, CompanyId, Action, EntityType, EntityId, Changes, IpAddress, CorrelationId)
    VALUES (@OccurredAt, @UserId, @CompanyId, @Action, @EntityType, @EntityId, @Changes, @IpAddress, @CorrelationId);
END;
