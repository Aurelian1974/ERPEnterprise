-- Regula de curs incrucisat a partenerului devine optionala (02.02, F4 3.3; macheta cf-08): lipsa ei inseamna regula implicita a firmei
-- (Fin.Fx.CrossCurrencyRule) si marja implicita (Fin.Fx.CrossCurrencyMarkupPercent). Profilurile cu valorile implicite de pana acum
-- (BNR din ziua platii, fara marja) trec pe regula firmei.
ALTER TABLE fin.PartnerFinanceProfile DROP CONSTRAINT CK_PartnerFinanceProfile_CrossCurrency;
ALTER TABLE fin.PartnerFinanceProfile DROP CONSTRAINT DF_PartnerFinanceProfile_CrossCurrencyRule;
ALTER TABLE fin.PartnerFinanceProfile DROP CONSTRAINT DF_PartnerFinanceProfile_CrossCurrencyMarkupPercent;
ALTER TABLE fin.PartnerFinanceProfile ALTER COLUMN CrossCurrencyRule tinyint NULL;
ALTER TABLE fin.PartnerFinanceProfile ALTER COLUMN CrossCurrencyMarkupPercent decimal(9,4) NULL;
GO

UPDATE fin.PartnerFinanceProfile SET CrossCurrencyRule = NULL WHERE CrossCurrencyRule = 1;
UPDATE fin.PartnerFinanceProfile SET CrossCurrencyMarkupPercent = NULL WHERE CrossCurrencyMarkupPercent = 0;
ALTER TABLE fin.PartnerFinanceProfile ADD CONSTRAINT CK_PartnerFinanceProfile_CrossCurrency CHECK ((CrossCurrencyRule IS NULL OR CrossCurrencyRule BETWEEN 1 AND 4)
    AND (CrossCurrencyMarkupPercent IS NULL OR (CrossCurrencyMarkupPercent >= 0 AND CrossCurrencyMarkupPercent < 100)));
