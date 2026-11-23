-- Avansurile de la clienti si catre furnizori devin partide nealocate in F3 (02.02, F3 4.2, 4.10): SettlementKind primeste 5 Avans client
-- si 6 Avans furnizor (plata fara factura, fara alocare automata la inregistrare; o consuma factura urmatoare, FIFO).
ALTER TABLE fin.TreasuryOperationType DROP CONSTRAINT CK_TreasuryOperationType_SettlementKind;
ALTER TABLE fin.TreasuryOperationType ADD CONSTRAINT CK_TreasuryOperationType_SettlementKind CHECK (SettlementKind BETWEEN 0 AND 6);
