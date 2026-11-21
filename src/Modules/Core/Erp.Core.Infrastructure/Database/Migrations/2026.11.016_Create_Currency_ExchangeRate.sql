-- Valute si cursuri valutare (02.01, 3.5 si 4.3; macheta 07b; deciziile lui Aurelian din 21.11.2026).
-- core.Currency: nomenclatorul ISO 4217; RON e moneda de baza (mereu activa), celelalte se activeaza la nevoie. Lista completa vine din
-- datele livrate (12_Currencies.sql); aici se adauga doar ce trebuie pentru cheile straine ale conturilor bancare existente.
-- core.ExchangeRate: cursurile pe zi, cu sursa BNR (preluate, nemodificabile) sau MANUAL (cu motiv); Rate = lei pentru Multiplier
-- unitati (BNR publica HUF, JPY, KRW etc. la 100 de unitati).
CREATE TABLE core.Currency (
    Code       char(3)       NOT NULL CONSTRAINT PK_Currency PRIMARY KEY,
    Name       nvarchar(100) NOT NULL,
    Decimals   tinyint       NOT NULL CONSTRAINT DF_Currency_Decimals DEFAULT (2),
    IsActive   bit           NOT NULL CONSTRAINT DF_Currency_IsActive DEFAULT (0),
    CreatedAt  datetime2(3)  NOT NULL CONSTRAINT DF_Currency_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy  int           NOT NULL,
    ModifiedAt datetime2(3)  NULL,
    ModifiedBy int           NULL,
    RowVersion rowversion    NOT NULL,
    CONSTRAINT CK_Currency_Code CHECK (Code LIKE '[A-Z][A-Z][A-Z]'),
    CONSTRAINT CK_Currency_Decimals CHECK (Decimals <= 4),
    CONSTRAINT CK_Currency_BaseActive CHECK (Code <> 'RON' OR IsActive = 1)
);
GO

INSERT INTO core.Currency (Code, Name, Decimals, IsActive, CreatedBy) VALUES ('RON', N'Leu românesc', 2, 1, 1);
-- Monedele conturilor bancare existente devin active, ca legatura sa ramana valida.
INSERT INTO core.Currency (Code, Name, Decimals, IsActive, CreatedBy)
SELECT DISTINCT x.Currency, x.Currency, 2, 1, 1
FROM (SELECT Currency FROM core.CompanyBankAccount UNION SELECT Currency FROM core.PartnerBankAccount) AS x
WHERE x.Currency <> 'RON';
GO

ALTER TABLE core.CompanyBankAccount ADD CONSTRAINT FK_CompanyBankAccount_Currency FOREIGN KEY (Currency) REFERENCES core.Currency (Code);
ALTER TABLE core.PartnerBankAccount ADD CONSTRAINT FK_PartnerBankAccount_Currency FOREIGN KEY (Currency) REFERENCES core.Currency (Code);
GO

CREATE TABLE core.ExchangeRate (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExchangeRate PRIMARY KEY,
    CurrencyCode char(3)           NOT NULL CONSTRAINT FK_ExchangeRate_Currency REFERENCES core.Currency (Code),
    RateDate     date              NOT NULL,
    Rate         decimal(19,6)     NOT NULL,
    Multiplier   int               NOT NULL CONSTRAINT DF_ExchangeRate_Multiplier DEFAULT (1),
    Source       varchar(10)       NOT NULL,
    Note         nvarchar(500)     NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_ExchangeRate_CreatedAt DEFAULT (sysutcdatetime()),
    CreatedBy    int               NOT NULL,
    ModifiedAt   datetime2(3)      NULL,
    ModifiedBy   int               NULL,
    RowVersion   rowversion        NOT NULL,
    CONSTRAINT CK_ExchangeRate_Rate CHECK (Rate > 0),
    CONSTRAINT CK_ExchangeRate_Multiplier CHECK (Multiplier > 0),
    CONSTRAINT CK_ExchangeRate_Source CHECK (Source IN ('BNR', 'MANUAL')),
    CONSTRAINT CK_ExchangeRate_ManualNote CHECK (Source = 'BNR' OR (Note IS NOT NULL AND LEN(Note) > 0)),
    CONSTRAINT CK_ExchangeRate_NotBase CHECK (CurrencyCode <> 'RON')
);
GO

-- Un curs pe moneda, zi si sursa; cautarea „ultimul curs pana la data” merge pe (moneda, data descrescator).
CREATE UNIQUE INDEX UX_ExchangeRate_Currency_Date_Source ON core.ExchangeRate (CurrencyCode, RateDate DESC, Source);
CREATE INDEX IX_ExchangeRate_RateDate ON core.ExchangeRate (RateDate) INCLUDE (CurrencyCode, Rate, Multiplier, Source);
GO

-- Cursurile unei zile salvate impreuna (core.usp_ExchangeRate_SaveBatch, SP-07).
CREATE TYPE core.ExchangeRateList AS TABLE (
    CurrencyCode char(3)       NOT NULL,
    RateDate     date          NOT NULL,
    Rate         decimal(19,6) NOT NULL,
    Multiplier   int           NOT NULL,
    PRIMARY KEY (CurrencyCode, RateDate)
);
GO
