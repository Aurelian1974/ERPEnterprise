CREATE OR ALTER PROCEDURE fin.usp_OpenItem_SetIndex
    @CompanyId          int,
    @Id                 bigint,
    @IndexCurrencyCode  char(3),
    @IndexedAmount      decimal(19,2)
AS
BEGIN
    -- Valuta si suma de referinta ale unei facturi in lei indexate (F4 3.1, 4.2 pasul 1), cu semnul partidei; restul de referinta e proportional
    -- cu restul in lei. Moneda de referinta trebuie sa existe si sa nu fie RON (51021).
    SET NOCOUNT ON;

    IF @IndexCurrencyCode = 'RON' OR NOT EXISTS (SELECT 1 FROM core.Currency WHERE Code = @IndexCurrencyCode) OR @IndexedAmount <= 0
        THROW 51021, N'Valuta de indexare nu este validă.', 1;

    UPDATE fin.OpenItem
    SET IndexCurrencyCode = @IndexCurrencyCode, IndexedAmount = SIGN(Amount) * @IndexedAmount,
        IndexedOpenAmount = ROUND(SIGN(Amount) * @IndexedAmount * OpenAmount / Amount, 2)
    WHERE Id = @Id AND CompanyId = @CompanyId AND CurrencyCode = 'RON';
END;
