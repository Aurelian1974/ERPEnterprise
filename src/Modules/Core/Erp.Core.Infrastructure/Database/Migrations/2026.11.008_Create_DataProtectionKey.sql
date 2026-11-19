-- Inelul de chei ASP.NET Core Data Protection (08, 5.12, decizia D22): sta in baza, deci intra in backup si e comun pentru
-- Erp.Host si Erp.Worker. Fiecare cheie (XML) e criptata cu certificatul instalarii inainte de a ajunge aici; fara certificat
-- (cheia de recuperare) cheile nu se pot citi. Tabela tehnica, folosita doar de IXmlRepository din Erp.Hosting.
CREATE TABLE core.DataProtectionKey (
    Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DataProtectionKey PRIMARY KEY,
    FriendlyName nvarchar(200)     NULL,
    Xml          nvarchar(max)     NOT NULL,
    CreatedAt    datetime2(3)      NOT NULL CONSTRAINT DF_DataProtectionKey_CreatedAt DEFAULT (sysutcdatetime())
);
