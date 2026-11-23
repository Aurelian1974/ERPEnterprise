-- Importul soldurilor initiale (02.02, F3 4.9): randurile validate, gata de inserat ca partide deschise (sold initial, tip 6, sau plata
-- nealocata, tip 5), cu semnul conventiei pe rol. Cheia de idempotenta: documentul sursa Finance.OpeningBalance, partenerul si „tip:numar”.
CREATE TYPE fin.OpeningItemList AS TABLE (
    RowNumber          int            NOT NULL PRIMARY KEY,
    PartnerId          int            NOT NULL,
    Role               tinyint        NOT NULL,
    Kind               tinyint        NOT NULL,
    LineKey            nvarchar(50)   NOT NULL,
    DocumentNo         nvarchar(50)   NOT NULL,
    DocumentDate       date           NOT NULL,
    DueDate            date           NOT NULL,
    CurrencyCode       char(3)        NOT NULL,
    ExchangeRate       decimal(19,6)  NOT NULL,
    Amount             decimal(19,2)  NOT NULL,
    AmountRon          decimal(19,2)  NOT NULL,
    VatAmountRon       decimal(19,2)  NOT NULL,
    IsVatOnCollection  bit            NOT NULL,
    GlAccountCode      nvarchar(20)   NULL
);
