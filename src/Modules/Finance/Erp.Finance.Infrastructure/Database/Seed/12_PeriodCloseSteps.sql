-- Pasii livrati ai listei de verificari la inchiderea lunii (02.02, C3 3.3; MIG-06): codul, denumirea si ordinea se actualizeaza la fiecare
-- versiune; activarea ramane cum a setat-o clientul. Pasii modulelor nelicentiate sau inca neimplementati nu apar.
SET NOCOUNT ON;

MERGE fin.PeriodCloseStepDefinition AS target
USING (VALUES
    ('PREV_PERIOD',       N'Luna anterioară e închisă', 10),
    ('EVENTS_IN_TRANSIT', N'Nu există documente validate încă necontate (evenimente în tranzit)', 20),
    ('CASH_DAYS',         N'Toate zilele de casă din lună sunt închise', 40),
    ('POSTING_QUEUE',     N'Coada „De contat” nu are documente datate în lună', 60),
    ('DRAFT_ENTRIES',     N'Nu există ciorne de note manuale sau recurente în lună', 70),
    ('COST_RECALC_PENDING', N'Coada de recalculare a costului stocurilor e goală pentru lună', 80),
    ('NEGATIVE_STOCK',    N'Niciun stoc negativ la sfârșitul lunii', 85),
    ('RETAIL_DISCHARGE',  N'Descărcarea la preț cu amănuntul (DA) e validată pentru gestiunile cu metoda K', 90),
    ('RECONCILIATION',    N'Concordanțele cu evidența operativă (clienți, furnizori, casă, stocuri) nu au diferențe', 110),
    ('ALT_MAPPING',       N'Liniile lunii sunt mapate în planurile alternative cu mapare completă', 120),
    ('ABNORMAL_BALANCES', N'Conturi cu sold pe partea greșită', 130)
) AS source (Code, Name, SortOrder)
ON target.Code = source.Code
WHEN MATCHED AND (target.Name <> source.Name OR target.SortOrder <> source.SortOrder) THEN UPDATE SET Name = source.Name, SortOrder = source.SortOrder
WHEN NOT MATCHED BY TARGET THEN INSERT (Code, Name, SortOrder, CreatedBy) VALUES (source.Code, source.Name, source.SortOrder, 1);
