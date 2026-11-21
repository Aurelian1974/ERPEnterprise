-- Configurare › Audit (02.01, 11; macheta 07h): cautarea pe interval de timp, cele mai noi intai. Indexul acopera filtrele
-- pe interval si pe firma; celelalte filtre (utilizator, entitate, actiune) se aplica pe randurile intervalului.
CREATE INDEX IX_AuditEntry_OccurredAt ON core.AuditEntry (OccurredAt DESC) INCLUDE (UserId, CompanyId, Action, EntityType, EntityId);
GO
