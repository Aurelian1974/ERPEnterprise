CREATE OR ALTER PROCEDURE core.usp_Maintenance_IndexesAndStatistics
AS
BEGIN
    -- Jobul SQL Agent „Mentenanță indecși” (08, 8.13): reorganizeaza indecsii cu fragmentare intre 10% si 30%, reconstruieste
    -- peste 30%, doar pentru indecsi cu peste 1.000 de pagini; ONLINE = ON doar pe editia Enterprise (EngineEdition 3);
    -- apoi actualizeaza statisticile modificate. Exceptie SP-08: fiecare index e o comanda DDL separata, deci bucla pe metadate.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Online bit = CASE WHEN CAST(SERVERPROPERTY('EngineEdition') AS int) = 3 THEN 1 ELSE 0 END;
    DECLARE @Work TABLE (Ordinal int IDENTITY(1,1) NOT NULL PRIMARY KEY, Command nvarchar(max) NOT NULL);

    INSERT INTO @Work (Command)
    SELECT N'ALTER INDEX ' + QUOTENAME(i.name) + N' ON ' + QUOTENAME(OBJECT_SCHEMA_NAME(i.object_id)) + N'.' + QUOTENAME(OBJECT_NAME(i.object_id))
         + CASE WHEN s.avg_fragmentation_in_percent > 30
                THEN N' REBUILD' + CASE WHEN @Online = 1 THEN N' WITH (ONLINE = ON)' ELSE N'' END
                ELSE N' REORGANIZE' END
         + N';'
    FROM sys.dm_db_index_physical_stats(DB_ID(), NULL, NULL, NULL, 'LIMITED') AS s
    JOIN sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
    JOIN sys.objects AS o ON o.object_id = i.object_id
    WHERE o.type = 'U' AND o.is_ms_shipped = 0
      AND i.index_id > 0 AND i.is_disabled = 0 AND i.name IS NOT NULL
      AND s.alloc_unit_type_desc = N'IN_ROW_DATA'
      AND s.page_count > 1000
      AND s.avg_fragmentation_in_percent >= 10
    ORDER BY o.object_id, i.index_id;

    INSERT INTO @Work (Command)
    SELECT N'UPDATE STATISTICS ' + QUOTENAME(OBJECT_SCHEMA_NAME(st.object_id)) + N'.' + QUOTENAME(OBJECT_NAME(st.object_id))
         + N' ' + QUOTENAME(st.name) + N';'
    FROM sys.stats AS st
    JOIN sys.objects AS o ON o.object_id = st.object_id
    CROSS APPLY sys.dm_db_stats_properties(st.object_id, st.stats_id) AS p
    WHERE o.type = 'U' AND o.is_ms_shipped = 0 AND p.modification_counter > 0
    ORDER BY st.object_id, st.stats_id;

    DECLARE @Ordinal int = 0, @Command nvarchar(max);
    WHILE 1 = 1
    BEGIN
        SET @Command = NULL;
        SELECT TOP (1) @Ordinal = w.Ordinal, @Command = w.Command
        FROM @Work AS w
        WHERE w.Ordinal > @Ordinal
        ORDER BY w.Ordinal;

        IF @Command IS NULL
            BREAK;

        EXEC sys.sp_executesql @Command;
    END;
END;
