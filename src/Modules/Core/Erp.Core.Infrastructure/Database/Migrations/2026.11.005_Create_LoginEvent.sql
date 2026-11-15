-- Jurnalul de autentificari (02.01, 3.5 si 9; 4.9: pastrat un an). Scris de Erp.Auth la fiecare incercare, reusita sau nu.
-- UserName = numele scris la autentificare (poate sa nu existe); UserId = contul gasit, daca exista.
CREATE TABLE core.LoginEvent (
    Id            bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoginEvent PRIMARY KEY,
    OccurredAt    datetime2(3)         NOT NULL,
    UserName      nvarchar(256)        NOT NULL,
    UserId        int                  NULL,
    Success       bit                  NOT NULL,
    FailureReason varchar(50)          NULL,
    IpAddress     varchar(45)          NULL,
    UserAgent     nvarchar(300)        NULL,
    CONSTRAINT CK_LoginEvent_FailureReason CHECK ((Success = 1 AND FailureReason IS NULL) OR (Success = 0 AND FailureReason IS NOT NULL))
);
GO

CREATE INDEX IX_LoginEvent_UserName_OccurredAt ON core.LoginEvent (UserName, OccurredAt);
CREATE INDEX IX_LoginEvent_OccurredAt ON core.LoginEvent (OccurredAt);
