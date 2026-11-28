-- Situatiile financiare livrate (02.02, C5 3.5, 3.6; decizia 2; MIG-06): bilantul (F10) si contul de profit si pierdere (F20) ca definitii de
-- raport needitabile (Kind 1), versionate pe data. Randurile sunt cele principale ale formularelor OMFP 1802/2014, cu formulele pe conturile
-- planului romanesc; corelatia cu modelele MF in vigoare se verifica la fiecare versiune (RAMAS-DE-FACUT, verificari externe). Randurile se
-- rescriu la fiecare rulare (definitiile livrate nu sunt ale clientului).
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Rows TABLE (Form varchar(10), RowCode varchar(30), Label nvarchar(300), Formula nvarchar(1000), IsTotal bit, Indent tinyint, SortOrder int);
INSERT INTO @Rows VALUES
    ('F10', 'F10.01', N'Imobilizări necorporale', N'MAX(0, SD(201..208) - SC(280) - SC(290))', 0, 1, 1),
    ('F10', 'F10.02', N'Imobilizări corporale', N'MAX(0, SD(211..235) - SC(281) - SC(291))', 0, 1, 2),
    ('F10', 'F10.03', N'Imobilizări financiare', N'MAX(0, SD(261..267) - SC(296))', 0, 1, 3),
    ('F10', 'F10.04', N'Active imobilizate – total', N'[F10.01] + [F10.02] + [F10.03]', 1, 0, 4),
    ('F10', 'F10.05', N'Stocuri', N'MAX(0, SN(301..388) - SC(391..398))', 0, 1, 5),
    ('F10', 'F10.06', N'Creanțe', N'SD(409..418) + SD(425) + SD(4282) + SD(431..438) + SD(441..448) + SD(461) + SD(473) - SC(491..496)', 0, 1, 6),
    ('F10', 'F10.07', N'Investiții pe termen scurt', N'MAX(0, SD(505..508) - SC(595..598))', 0, 1, 7),
    ('F10', 'F10.08', N'Casa și conturi la bănci', N'SD(5112..5125) + SD(531..542)', 0, 1, 8),
    ('F10', 'F10.09', N'Active circulante – total', N'[F10.05] + [F10.06] + [F10.07] + [F10.08]', 1, 0, 9),
    ('F10', 'F10.10', N'Cheltuieli în avans', N'SD(471)', 0, 0, 10),
    ('F10', 'F10.11', N'Datorii ce trebuie plătite într-o perioadă de până la un an', N'SC(401..408) + SC(419) + SC(421..427) + SC(4281) + SC(431..438) + SC(441..448) + SC(455..457) + SC(462) + SC(473) + SC(519)', 0, 0, 11),
    ('F10', 'F10.12', N'Active circulante nete / datorii curente nete', N'[F10.09] + [F10.10] - [F10.11]', 1, 0, 12),
    ('F10', 'F10.13', N'Total active minus datorii curente', N'[F10.04] + [F10.12]', 1, 0, 13),
    ('F10', 'F10.14', N'Datorii ce trebuie plătite într-o perioadă mai mare de un an', N'SC(161..169)', 0, 0, 14),
    ('F10', 'F10.15', N'Provizioane', N'SC(151)', 0, 0, 15),
    ('F10', 'F10.16', N'Venituri în avans', N'SC(472) + SC(475)', 0, 0, 16),
    ('F10', 'F10.17', N'Capital subscris', N'SC(1011..1012)', 0, 1, 17),
    ('F10', 'F10.18', N'Prime de capital, rezerve din reevaluare și rezerve', N'SN(104..106) * -1', 0, 1, 18),
    ('F10', 'F10.19', N'Rezultatul reportat', N'SN(117) * -1', 0, 1, 19),
    ('F10', 'F10.20', N'Rezultatul exercițiului', N'(SN(121) + SN(6) + SN(7)) * -1', 0, 1, 20),
    ('F10', 'F10.21', N'Repartizarea profitului', N'SD(129)', 0, 1, 21),
    ('F10', 'F10.22', N'Capitaluri proprii – total', N'[F10.17] + [F10.18] + [F10.19] + [F10.20] - [F10.21]', 1, 0, 22),
    ('F10', 'F10.90', N'Control: total activ', N'[F10.04] + [F10.09] + [F10.10]', 1, 0, 90),
    ('F10', 'F10.91', N'Control: capitaluri și datorii', N'[F10.11] + [F10.14] + [F10.15] + [F10.16] + [F10.22]', 1, 0, 91),
    ('F20', 'F20.01', N'Cifra de afaceri netă', N'RC(701..708) - RD(709)', 0, 0, 1),
    ('F20', 'F20.02', N'Variația stocurilor și producția imobilizată', N'RC(711..725) - RD(711..725)', 0, 1, 2),
    ('F20', 'F20.03', N'Alte venituri din exploatare', N'RC(741..758) + RC(781)', 0, 1, 3),
    ('F20', 'F20.10', N'Venituri din exploatare – total', N'[F20.01] + [F20.02] + [F20.03]', 1, 0, 10),
    ('F20', 'F20.11', N'Cheltuieli cu materiile prime, materialele și mărfurile', N'RD(601..608) - RC(609)', 0, 1, 11),
    ('F20', 'F20.12', N'Cheltuieli cu personalul', N'RD(641..646)', 0, 1, 12),
    ('F20', 'F20.13', N'Ajustări de valoare (amortizări, depreciere)', N'RD(681)', 0, 1, 13),
    ('F20', 'F20.14', N'Alte cheltuieli de exploatare', N'RD(611..635) + RD(651..658)', 0, 1, 14),
    ('F20', 'F20.20', N'Cheltuieli de exploatare – total', N'[F20.11] + [F20.12] + [F20.13] + [F20.14]', 1, 0, 20),
    ('F20', 'F20.21', N'Rezultatul din exploatare', N'[F20.10] - [F20.20]', 1, 0, 21),
    ('F20', 'F20.30', N'Venituri financiare', N'RC(761..768) + RC(786)', 0, 1, 30),
    ('F20', 'F20.31', N'Cheltuieli financiare', N'RD(661..668) + RD(686)', 0, 1, 31),
    ('F20', 'F20.32', N'Rezultatul financiar', N'[F20.30] - [F20.31]', 1, 0, 32),
    ('F20', 'F20.40', N'Venituri totale', N'[F20.10] + [F20.30]', 1, 0, 40),
    ('F20', 'F20.41', N'Cheltuieli totale', N'[F20.20] + [F20.31]', 1, 0, 41),
    ('F20', 'F20.42', N'Rezultatul brut', N'[F20.40] - [F20.41]', 1, 0, 42),
    ('F20', 'F20.43', N'Impozitul pe profit și alte impozite', N'RD(691) + RD(698) - RC(691)', 0, 0, 43),
    ('F20', 'F20.44', N'Rezultatul net al exercițiului', N'[F20.42] - [F20.43]', 1, 0, 44);

MERGE fin.ReportDefinition AS target
USING (VALUES ('F10', N'Bilanț (F10)'), ('F20', N'Contul de profit și pierdere (F20)')) AS source (Code, Name)
ON target.Code = source.Code AND target.Kind = 1 AND target.Version = 1
WHEN MATCHED AND target.Name <> source.Name THEN UPDATE SET Name = source.Name, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = 1
WHEN NOT MATCHED BY TARGET THEN INSERT (Code, Name, Kind, Version, ValidFrom, CreatedBy) VALUES (source.Code, source.Name, 1, 1, '2015-01-01', 1);

DELETE r FROM fin.ReportRow AS r JOIN fin.ReportDefinition AS d ON d.Id = r.DefinitionId WHERE d.Kind = 1 AND d.Code IN ('F10', 'F20') AND d.Version = 1;
INSERT INTO fin.ReportRow (DefinitionId, RowCode, Label, Formula, IsTotal, Indent, SortOrder)
SELECT d.Id, r.RowCode, r.Label, r.Formula, r.IsTotal, r.Indent, r.SortOrder
FROM @Rows AS r JOIN fin.ReportDefinition AS d ON d.Code = r.Form AND d.Kind = 1 AND d.Version = 1;
