-- Stocuri S3.4 (02.05, S3 3.6, 3.7): metoda de descarcare cu care s-a contat fiecare iesire dintr-o gestiune la pret cu amanuntul (1 prin K,
-- 2 la costul ei, cu adaosul si TVA-ul descarcate pe loc; predarile in alta gestiune mereu 2). Descarcarea lunara si concordanta citesc metoda
-- de pe miscare, nu de pe gestiune, ca schimbarea metodei sa nu schimbe trecutul.
ALTER TABLE inv.StockMovement ADD RetailDischargeMethod tinyint NULL
    CONSTRAINT CK_StockMovement_RetailDischargeMethod CHECK (RetailDischargeMethod IS NULL OR RetailDischargeMethod IN (1, 2));
GO
