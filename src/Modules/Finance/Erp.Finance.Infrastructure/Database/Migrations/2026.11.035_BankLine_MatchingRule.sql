-- Regula care a explicat o linie de extras (02.02, F2 3.5; macheta f2-04: numarul de folosiri al fiecarei reguli). Se pune la potrivirea
-- automata si se sterge cand linia primeste alta explicatie.
ALTER TABLE fin.BankStatementLine ADD MatchingRuleId int NULL CONSTRAINT FK_BankStatementLine_MatchingRule REFERENCES fin.MatchingRule (Id);
GO

CREATE INDEX IX_BankStatementLine_MatchingRuleId ON fin.BankStatementLine (MatchingRuleId) WHERE MatchingRuleId IS NOT NULL;
GO
