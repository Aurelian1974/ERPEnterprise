CREATE OR ALTER FUNCTION core.fn_NameKey
(
    @Name nvarchar(250)
)
RETURNS TABLE
AS
RETURN
    -- Cheia de comparare a denumirilor pentru dubluri (02.01, 3.6 punctul 7): litere mari, fara punctuatie, spatii si forma juridica
    -- (SRL, SA, SNC, SCS, PFA, II, IF); diacriticele se ignora la comparare (collation CI_AI pe coloana temporara).
    SELECT REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
               N' ' + UPPER(@Name) + N' ',
               N'S.R.L.', N' '), N'S.A.', N' '), N' SRL ', N' '), N' SA ', N' '), N' SNC ', N' '), N' SCS ', N' '), N' PFA ', N' '),
               N' II ', N' '), N' IF ', N' '), N'.', N''), N',', N''), N' ', N'') AS NameKey;
