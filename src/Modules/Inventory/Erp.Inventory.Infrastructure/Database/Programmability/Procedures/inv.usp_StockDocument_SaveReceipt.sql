CREATE OR ALTER PROCEDURE inv.usp_StockDocument_SaveReceipt
    @CompanyId  int,
    @Id         int,
    @Notes      nvarchar(1000) = NULL,
    @Lines      inv.tt_ReceiptLine READONLY,
    @RowVersion binary(8),
    @UserId     int
AS
BEGIN
    -- Cantitatile primite pe nota de primire si rezolvarea fiecarei diferente (02.05, S2 3.4): 1 ramane in tranzit, 2 retur la expeditor, 3 lipsa
    -- imputabila, 4 lipsa neimputabila, 5 plus la primire. O lipsa cere rezolvarea 1–4; un plus cere rezolvarea 5 (54109). Coduri: 54001, 54104,
    -- 54117, 54118.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status tinyint = (SELECT Status FROM inv.StockDocument WITH (UPDLOCK, ROWLOCK) WHERE Id = @Id AND CompanyId = @CompanyId AND DocumentType = 'NR');
    IF @Status IS NULL
        THROW 54001, N'Nota de primire nu există în firma curentă.', 1;
    IF @Status <> 1
        THROW 54118, N'Nota de primire nu mai este ciornă.', 1;
    IF EXISTS (SELECT 1 FROM @Lines WHERE ReceivedQtyBase < 0)
       OR EXISTS (SELECT 1 FROM @Lines AS t WHERE NOT EXISTS (SELECT 1 FROM inv.StockDocumentLine AS l WHERE l.Id = t.LineId AND l.DocumentId = @Id))
        THROW 54104, N'Cantitatea primită nu poate fi negativă, pe liniile notei.', 1;
    IF EXISTS (SELECT 1 FROM @Lines AS t JOIN inv.StockDocumentLine AS l ON l.Id = t.LineId
               WHERE (t.ReceivedQtyBase < l.QtyBase AND ISNULL(t.Resolution, 0) NOT IN (1, 2, 3, 4))
                  OR (t.ReceivedQtyBase > l.QtyBase AND ISNULL(t.Resolution, 0) <> 5))
        THROW 54109, N'Fiecare diferență cere rezolvarea ei: lipsa rămâne în tranzit, se returnează sau se descarcă; plusul se încarcă.', 1;

    UPDATE l
    SET ReceivedQtyBase = t.ReceivedQtyBase, DifferenceQtyBase = l.QtyBase - t.ReceivedQtyBase,
        DifferenceResolution = CASE WHEN t.ReceivedQtyBase = l.QtyBase THEN NULL ELSE t.Resolution END
    FROM inv.StockDocumentLine AS l
    JOIN @Lines AS t ON t.LineId = l.Id;

    UPDATE inv.StockDocument SET Notes = @Notes, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId WHERE Id = @Id AND RowVersion = @RowVersion;
    IF @@ROWCOUNT = 0
        THROW 54117, N'Nota de primire a fost modificată de alt utilizator; reîncărcați datele.', 1;

    SELECT Id, RowVersion FROM inv.StockDocument WHERE Id = @Id;
END;
