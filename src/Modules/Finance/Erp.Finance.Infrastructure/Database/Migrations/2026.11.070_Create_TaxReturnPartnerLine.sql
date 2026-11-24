-- Liniile pe partener ale declaratiilor informative D394 si D390 (02.02, C4 3.3, 4.2): sectiunea (D394) sau codul operatiunii (D390),
-- partenerul la data calculului, cota, numarul de documente, baza si TVA-ul; problema gasita (cod fiscal lipsa, cod de TVA UE lipsa).
CREATE TABLE fin.TaxReturnPartnerLine (
    Id                  int IDENTITY(1,1) NOT NULL CONSTRAINT PK_TaxReturnPartnerLine PRIMARY KEY,
    ReturnId            int               NOT NULL CONSTRAINT FK_TaxReturnPartnerLine_Return REFERENCES fin.TaxReturn (Id),
    Section             varchar(5)        NOT NULL,
    PartnerId           int               NULL,
    PartnerFiscalCode   nvarchar(30)      NULL,
    PartnerCountryCode  char(2)           NULL,
    PartnerName         nvarchar(200)     NULL,
    VatRatePercent      decimal(5,2)      NOT NULL,
    Documents           int               NOT NULL,
    BaseRon             decimal(19,2)     NOT NULL,
    VatRon              decimal(19,2)     NOT NULL,
    Issue               nvarchar(200)     NULL
);

CREATE INDEX IX_TaxReturnPartnerLine_Return ON fin.TaxReturnPartnerLine (ReturnId);
