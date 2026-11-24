-- Facturile in lei indexate la o valuta (02.02, F4 4.2): lotul de alocari primeste suma de referinta stinsa si marcajul de indexare, ca
-- diferenta sa mearga pe 768 / 668 (Source = 4). Restul de referinta (IndexedOpenAmount) ramane proportional cu restul in lei.
DROP PROCEDURE IF EXISTS fin.usp_Allocation_CreateBatch;
DROP TYPE fin.AllocationList;
CREATE TYPE fin.AllocationList AS TABLE (
    RowNo             int            NOT NULL PRIMARY KEY,
    DebitOpenItemId   bigint         NOT NULL,
    CreditOpenItemId  bigint         NOT NULL,
    DebitAmount       decimal(19,2)  NOT NULL,
    CreditAmount      decimal(19,2)  NOT NULL,
    AmountRonDebit    decimal(19,2)  NOT NULL,
    AmountRonCredit   decimal(19,2)  NOT NULL,
    AllocationDate    date           NOT NULL,
    Source            tinyint        NOT NULL,
    CompensationId    int            NULL,
    WriteOffId        int            NULL,
    CommercialPaperId int            NULL,
    CrossRate         decimal(19,6)  NULL,
    CrossRateSource   tinyint        NULL,
    DifferenceRon     decimal(19,2)  NOT NULL,
    FxOpenItemId      bigint         NULL,
    IsIndexed         bit            NOT NULL,
    IndexedAmount     decimal(19,2)  NULL
);
