# 📋 Audit System MVP - Design & Architecture Analysis

**Project:** ValyanERP  
**Feature:** Comprehensive Audit Trail System (MVP)  
**Date:** 2026-01-09  
**Status:** 🔄 In Progress - Phase 1 (Design)

---

## 🎯 Executive Summary

Implementare sistem comprehensive audit pentru tracking complet al modificărilor în aplicație:
- **WHO** tracked via UserId + Session
- **WHAT** tracked via EntityType + EntityId  
- **WHEN** tracked via Timestamp (Bucharest timezone)
- **WHERE** tracked via IPAddress + UserAgent
- **CHANGES** tracked via Old/New values în tabele separate (normalized)

---

## 📊 User Decisions (Confirmed)

### ✅ Scope & Coverage
| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Entities to Audit** | ✅ ALL tables | Comprehensive coverage as tables grow |
| **Granularity** | ✅ Full record | Simpler implementation, complete history |
| **SystemParameters** | ✅ YES | Critical config changes tracking |
| **Session Operations** | ✅ YES | Security - login/logout tracking |

### ✅ Data Storage Strategy
| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Storage Model** | ✅ Normalized (2 tables) | AuditLogs + AuditLogDetails |
| **Retention Policy** | ✅ Time-based | Delete after 1/2/5 years (configurable) |
| **JSON Library** | ✅ System.Text.Json | Built-in, performant |
| **Diff Algorithm** | ✅ Simple JSON comparison | MVP simplicity |

### ✅ Performance & Security
| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Execution Mode** | ✅ Sync in transaction | Data consistency critical |
| **Capture Strategy** | ✅ Hybrid | Auto CRUD + Manual bulk |
| **IP Tracking** | ✅ YES | Security analysis |
| **Session Linking** | ✅ YES | Link to Sessions table |

### ✅ Privacy & Masking
| Decision | Choice | Impact |
|----------|--------|--------|
| **Passwords** | ✅ ALWAYS mask | Security requirement |
| **CNP** | ✅ Mask | GDPR compliance (show only last 4) |
| **Email** | ✅ Partial | `u***@domain.com` format |
| **View Permissions** | ✅ User own + Admin all | Privacy + transparency |

### ✅ UI Features
| Decision | Choice | Details |
|----------|--------|---------|
| **Real-time Updates** | ✅ SignalR | Live audit feed |
| **Export Formats** | ✅ Excel + CSV | Both formats supported |
| **Filtering** | ✅ Advanced | Entity, User, Date, Operation |

---

## 🗄️ Database Schema Design

### Table 1: AuditLogs (Master Table)

```sql
CREATE TABLE [dbo].[AuditLogs] (
    -- Primary Key (UNIQUEIDENTIFIER for consistency)
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- Entity Tracking (WHO/WHAT)
    [EntityType] NVARCHAR(100) NOT NULL,        -- e.g., "Persoane", "Users", "SystemParameters"
    [EntityId] NVARCHAR(450) NOT NULL,          -- GUID or composite key (flexible)
    [OperationType] NVARCHAR(20) NOT NULL,      -- "Create", "Update", "Delete"
    
    -- User Context (WHO)
    [UserId] UNIQUEIDENTIFIER NOT NULL,         -- FK to AspNetUsers
    [UserEmail] NVARCHAR(256) NOT NULL,         -- Denormalized for performance
    [UserFullName] NVARCHAR(200) NULL,          -- Denormalized for display
    
    -- Session Context (WHERE/WHEN)
    [SessionId] UNIQUEIDENTIFIER NULL,          -- FK to Sessions table
    [IPAddress] NVARCHAR(45) NULL,              -- IPv4 or IPv6
    [UserAgent] NVARCHAR(500) NULL,             -- Browser info
    
    -- Timestamp (WHEN)
    [Timestamp] DATETIME2 NOT NULL DEFAULT (GETDATE() AT TIME ZONE 'UTC' AT TIME ZONE 'GTB Standard Time'),
    
    -- Metadata
    [TableName] NVARCHAR(100) NOT NULL,         -- Actual SQL table name
    [ChangedFieldsCount] INT NOT NULL DEFAULT 0, -- Number of fields changed
    [Notes] NVARCHAR(MAX) NULL,                 -- Optional manual notes
    
    -- Indexes
    CONSTRAINT [FK_AuditLogs_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers]([Id]),
    CONSTRAINT [FK_AuditLogs_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [dbo].[Sessions]([Id])
);

-- Performance Indexes
CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [dbo].[AuditLogs] ([EntityType], [EntityId]);
CREATE INDEX [IX_AuditLogs_UserId_Timestamp] ON [dbo].[AuditLogs] ([UserId], [Timestamp] DESC);
CREATE INDEX [IX_AuditLogs_Timestamp] ON [dbo].[AuditLogs] ([Timestamp] DESC);
CREATE INDEX [IX_AuditLogs_OperationType] ON [dbo].[AuditLogs] ([OperationType]);
CREATE INDEX [IX_AuditLogs_SessionId] ON [dbo].[AuditLogs] ([SessionId]);
```

### Table 2: AuditLogDetails (Normalized Field-Level Storage)

```sql
CREATE TABLE [dbo].[AuditLogDetails] (
    -- Primary Key (UNIQUEIDENTIFIER for consistency)
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- Link to Master
    [AuditLogId] UNIQUEIDENTIFIER NOT NULL,
    
    -- Field-Level Change Tracking
    [FieldName] NVARCHAR(100) NOT NULL,         -- Property name (e.g., "FirstName", "Email")
    [OldValue] NVARCHAR(MAX) NULL,              -- Previous value (masked if sensitive)
    [NewValue] NVARCHAR(MAX) NULL,              -- New value (masked if sensitive)
    [DataType] NVARCHAR(50) NULL,               -- "string", "int", "datetime", "bool"
    [IsSensitive] BIT NOT NULL DEFAULT 0,       -- Flag for masked fields
    
    -- Foreign Key
    CONSTRAINT [FK_AuditLogDetails_AuditLogId] FOREIGN KEY ([AuditLogId]) REFERENCES [dbo].[AuditLogs]([Id]) ON DELETE CASCADE
);

-- Performance Index
CREATE INDEX [IX_AuditLogDetails_AuditLogId] ON [dbo].[AuditLogDetails] ([AuditLogId]);
CREATE INDEX [IX_AuditLogDetails_FieldName] ON [dbo].[AuditLogDetails] ([FieldName]);
```

---

## 🔧 Stored Procedures Design

### 1. sp_AuditLogs_Create
**Purpose:** Insert new audit log with details  
**Parameters:** EntityType, EntityId, OperationType, UserId, SessionId, IPAddress, UserAgent, ChangedFields (JSON array)  
**Returns:** New AuditLogId (UNIQUEIDENTIFIER)

### 2. sp_AuditLogs_GetByEntity
**Purpose:** Retrieve all audit logs for specific entity  
**Parameters:** EntityType, EntityId, PageNumber, PageSize  
**Returns:** Paginated audit logs with details

### 3. sp_AuditLogs_GetByUser
**Purpose:** Retrieve all audit logs for specific user  
**Parameters:** UserId, StartDate, EndDate, PageNumber, PageSize  
**Returns:** Paginated user activity

### 4. sp_AuditLogs_GetBySession
**Purpose:** Retrieve all audit logs for specific session  
**Parameters:** SessionId  
**Returns:** Session activity timeline

### 5. sp_AuditLogs_GetStatistics
**Purpose:** Calculate audit statistics  
**Parameters:** StartDate, EndDate  
**Returns:** Stats (total changes, by operation type, by entity type, top users)

### 6. sp_AuditLogs_Search
**Purpose:** Advanced search with multiple filters  
**Parameters:** EntityType, UserId, OperationType, StartDate, EndDate, SearchTerm, PageNumber, PageSize  
**Returns:** Filtered audit logs

### 7. sp_AuditLogs_DeleteOld
**Purpose:** Cleanup old audit logs based on retention policy  
**Parameters:** RetentionDays (from SystemParameters)  
**Returns:** Number of deleted records

### 8. sp_AuditLogs_GetUserOwnActivity
**Purpose:** User views their own audit trail  
**Parameters:** UserId, StartDate, EndDate, PageNumber, PageSize  
**Returns:** User's own changes only

---

## 🏗️ C# Architecture

### Namespace Structure
```
Features/
├── Infrastructure/
│   └── Audit/
│       ├── Models/
│       │   ├── AuditLog.cs
│       │   ├── AuditLogDetail.cs
│       │   ├── AuditLogEntry.cs (DTO for creation)
│       │   └── AuditStatistics.cs
│       ├── Repositories/
│       │   ├── IAuditRepository.cs
│       │   └── AuditRepository.cs
│       ├── Services/
│       │   ├── IAuditService.cs
│       │   ├── AuditService.cs
│       │   └── SensitiveDataMasker.cs
│       └── Extensions/
│           └── AuditExtensions.cs
```

### Key Interfaces

#### IAuditService
```csharp
public interface IAuditService
{
    // CRUD Audit Logging
    Task<Guid> LogCreateAsync<T>(string entityType, string entityId, T newValue, Guid userId, Guid? sessionId = null, string? ipAddress = null, string? userAgent = null);
    Task<Guid> LogUpdateAsync<T>(string entityType, string entityId, T oldValue, T newValue, Guid userId, Guid? sessionId = null, string? ipAddress = null, string? userAgent = null);
    Task<Guid> LogDeleteAsync<T>(string entityType, string entityId, T deletedValue, Guid userId, Guid? sessionId = null, string? ipAddress = null, string? userAgent = null);
    
    // Queries
    Task<IEnumerable<AuditLog>> GetByEntityAsync(string entityType, string entityId, int pageNumber = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetByUserAsync(Guid userId, DateTime? startDate = null, DateTime? endDate = null, int pageNumber = 1, int pageSize = 50);
    Task<IEnumerable<AuditLog>> GetBySessionAsync(Guid sessionId);
    Task<AuditLog?> GetByIdAsync(Guid id);
    
    // Statistics
    Task<AuditStatistics> GetStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null);
    
    // Search
    Task<IEnumerable<AuditLog>> SearchAsync(string? entityType = null, Guid? userId = null, string? operationType = null, DateTime? startDate = null, DateTime? endDate = null, string? searchTerm = null, int pageNumber = 1, int pageSize = 50);
    
    // Cleanup
    Task<int> DeleteOldLogsAsync(int retentionDays);
}
```

#### SensitiveDataMasker
```csharp
public class SensitiveDataMasker
{
    // Password: Always "*****"
    public string MaskPassword(string? value);
    
    // CNP: Show only last 4 digits
    public string MaskCNP(string? cnp);
    
    // Email: u***@domain.com
    public string MaskEmail(string? email);
    
    // Detect sensitive fields by name
    public bool IsSensitiveField(string fieldName);
    
    // Mask value based on field name
    public string MaskValue(string fieldName, string? value);
}
```

---

## 🔄 Integration Strategy (Hybrid Approach)

### Auto-Capture (Repository Level)
**Target:** Standard CRUD operations in existing repositories  
**Repositories to modify:**
- `PersoaneRepository` (Create, Update, Delete)
- `SystemParametersRepository` (Update only - critical)
- `UsersRepository` (if exists - Create, Update, Delete)
- `RolesRepository` (if exists - Create, Update, Delete)

**Implementation Pattern:**
```csharp
public async Task<int> CreateAsync(CreatePersoanaDto dto)
{
    try
    {
        using var connection = _context.CreateConnection();
        
        // Execute creation
        var result = await connection.QueryFirstAsync<int>(
            "sp_Persoane_Create",
            dto,
            commandType: CommandType.StoredProcedure);
        
        // Auto-capture audit (if userId available in context)
        if (_httpContextAccessor?.HttpContext?.User?.Identity?.IsAuthenticated == true)
        {
            var userId = GetCurrentUserId();
            var sessionId = GetCurrentSessionId();
            var ipAddress = GetClientIP();
            var userAgent = GetUserAgent();
            
            await _auditService.LogCreateAsync(
                "Persoane", 
                result.ToString(), 
                dto, 
                userId, 
                sessionId, 
                ipAddress, 
                userAgent);
        }
        
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error creating Persoana");
        throw;
    }
}
```

### Manual Capture (Service Level)
**Target:** Bulk operations, complex business logic  
**Use cases:**
- Batch imports
- Data migrations
- Scheduled jobs
- Admin override operations

**Implementation Pattern:**
```csharp
public async Task ImportPersonsAsync(List<ImportPersoanaDto> batch, Guid userId)
{
    // Bulk insert without audit
    await _repository.BulkInsertAsync(batch);
    
    // Manual audit log for entire batch
    await _auditService.LogCreateAsync(
        "Persoane", 
        "BULK_IMPORT", 
        new { Count = batch.Count, Timestamp = DateTime.UtcNow }, 
        userId,
        notes: $"Imported {batch.Count} persons via bulk operation");
}
```

---

## 📈 Performance Analysis

### Expected Volume (Year 1)
| Entity | Daily Changes | Annual Volume | Storage Est. |
|--------|---------------|---------------|--------------|
| Persoane | 50 | 18,250 | ~2 MB |
| Users | 5 | 1,825 | ~200 KB |
| SystemParameters | 2 | 730 | ~100 KB |
| Sessions | 200 | 73,000 | ~10 MB |
| **TOTAL** | ~260 | ~94,000 | ~12 MB/year |

### Retention Impact
| Retention | Total Records | Storage Est. | Query Performance |
|-----------|---------------|--------------|-------------------|
| 1 year | ~94,000 | ~12 MB | ⚡ Excellent |
| 2 years | ~188,000 | ~24 MB | ⚡ Good |
| 5 years | ~470,000 | ~60 MB | ⚙️ Moderate (needs indexes) |

### Performance Optimizations
1. ✅ **UNIQUEIDENTIFIER** with NEWSEQUENTIALID() for sequential generation (index-friendly)
2. ✅ **6 indexes** on AuditLogs for fast queries
3. ✅ **Sync execution** in transaction (5-10ms overhead per operation)
4. ✅ **Paginated queries** (default 50 records per page)
5. ✅ **Denormalized UserEmail/FullName** (avoid JOINs on display)
6. ✅ **CASCADE DELETE** on AuditLogDetails (cleanup efficiency)
7. ✅ **Scheduled cleanup job** via `sp_AuditLogs_DeleteOld`

---

## 🔐 Security & Privacy Considerations

### GDPR Compliance
| Requirement | Implementation |
|-------------|----------------|
| **Right to be Forgotten** | Cannot delete audit logs (legal requirement), but can anonymize user data |
| **Data Minimization** | Mask sensitive fields (CNP, passwords) |
| **Purpose Limitation** | Audit data used ONLY for security/compliance |
| **Retention Limits** | Auto-delete after retention period (1-5 years) |

### Access Control Matrix
| User Role | View Own Activity | View All Activity | Export | Delete |
|-----------|-------------------|-------------------|--------|--------|
| **Regular User** | ✅ YES | ❌ NO | ✅ Own only | ❌ NO |
| **Admin** | ✅ YES | ✅ YES | ✅ All | ⚠️ Soft delete only |
| **System** | N/A | ✅ YES | ✅ YES | ✅ Cleanup job only |

### Sensitive Fields Detection
**Auto-detected by field name:**
- `Password`, `PasswordHash`, `ConfirmPassword`
- `CNP`, `SSN`, `TaxId`
- `CreditCard`, `CardNumber`, `CVV`
- `Token`, `ApiKey`, `Secret`

**Masking Rules:**
```csharp
Password      → "*****" (always)
CNP           → "******1234" (last 4 digits)
Email         → "u***@domain.com" (partial)
CreditCard    → "****-****-****-1234" (last 4)
```

---

## 🎨 UI/UX Design (Blazor Admin Page)

### Page: /administrare/audit-logs

#### Layout Structure
```
┌─────────────────────────────────────────────────────────┐
│ 📊 Audit Trail System                      [Export ▼]   │
├─────────────────────────────────────────────────────────┤
│ Filters:                                                 │
│ [Entity Type ▼] [User ▼] [Operation ▼] [Date Range]    │
│ [Search...] [Apply] [Reset]                             │
├─────────────────────────────────────────────────────────┤
│ Syncfusion SfGrid (Paginated, Sortable, Filterable)     │
│ ┌───┬──────────┬───────────┬──────────┬──────────────┐ │
│ │ # │ When     │ Who       │ Action   │ Entity       │ │
│ ├───┼──────────┼───────────┼──────────┼──────────────┤ │
│ │ 1 │ 10:30 AM │ John Doe  │ Update   │ Persoane #23 │ │
│ │ 2 │ 10:25 AM │ Jane Smith│ Create   │ Users #45    │ │
│ │ 3 │ 10:20 AM │ John Doe  │ Delete   │ Persoane #12 │ │
│ └───┴──────────┴───────────┴──────────┴──────────────┘ │
│ [← Previous] Page 1 of 10 [Next →]                      │
└─────────────────────────────────────────────────────────┘
```

#### Detail Modal (Click on row)
```
┌─────────────────────────────────────────────────────────┐
│ Audit Log Details #12345                          [✕]   │
├─────────────────────────────────────────────────────────┤
│ Operation: Update Persoana #23                          │
│ User: John Doe (john.doe@valyan.ro)                     │
│ Session: 550e8400-e29b-41d4-a716-446655440000           │
│ IP Address: 192.168.1.105                               │
│ User Agent: Chrome 120 on Windows                       │
│ Timestamp: 2026-01-09 10:30:45                          │
├─────────────────────────────────────────────────────────┤
│ 📝 Changed Fields (5):                                  │
│                                                          │
│ ┌─────────────┬────────────────┬────────────────────┐  │
│ │ Field       │ Old Value      │ New Value          │  │
│ ├─────────────┼────────────────┼────────────────────┤  │
│ │ FirstName   │ Ion            │ Ioan               │  │
│ │ Email       │ i***@email.com │ ioan***@email.com  │  │
│ │ CNP         │ ******1234     │ ******5678         │  │
│ │ IsActive    │ true           │ false              │  │
│ │ UpdatedAt   │ 2026-01-01     │ 2026-01-09         │  │
│ └─────────────┴────────────────┴────────────────────┘  │
│                                                          │
│ [Export to Excel] [Close]                               │
└─────────────────────────────────────────────────────────┘
```

---

## 📊 Statistics Dashboard (Optional for MVP)

### Quick Stats Cards
```
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ Total Changes    │ │ Today's Activity │ │ Active Users     │
│   94,523         │ │      47          │ │      12          │
└──────────────────┘ └──────────────────┘ └──────────────────┘
```

### Charts (Syncfusion)
- **Line Chart:** Changes over time (last 30 days)
- **Pie Chart:** Changes by operation type (Create/Update/Delete)
- **Bar Chart:** Top 10 most modified entities

---

## 🧪 Testing Strategy (FAZA 7)

### Unit Tests (40+ tests)
- [ ] `AuditService.LogCreateAsync()` - successful creation
- [ ] `AuditService.LogUpdateAsync()` - field-level diff
- [ ] `AuditService.LogDeleteAsync()` - soft delete tracking
- [ ] `SensitiveDataMasker.MaskPassword()` - always masked
- [ ] `SensitiveDataMasker.MaskCNP()` - last 4 digits
- [ ] `SensitiveDataMasker.MaskEmail()` - partial masking
- [ ] `AuditRepository.GetByEntityAsync()` - pagination
- [ ] `AuditRepository.SearchAsync()` - complex filters

### Integration Tests (E2E)
- [ ] Create Persoana → Verify audit log created
- [ ] Update SystemParameter → Verify audit log with old/new values
- [ ] Delete Session → Verify audit log with all session data
- [ ] User views own activity → Verify correct filtering
- [ ] Admin views all activity → Verify full access
- [ ] Export to Excel → Verify file generation
- [ ] Cleanup job → Verify old logs deleted

### Performance Tests
- [ ] 1000 audit logs insertion → <500ms
- [ ] Query 10,000 logs with filters → <200ms
- [ ] Export 1000 logs to Excel → <2 seconds
- [ ] Sync audit overhead → <10ms per operation

---

## 📝 Documentation Deliverables (FAZA 7)

### For Administrators
- **Admin Guide:** How to view audit logs, interpret changes, export data
- **Retention Policy:** How to configure cleanup schedule
- **GDPR Compliance:** Data retention, anonymization procedures

### For Developers
- **Integration Guide:** How to add audit to new repositories
- **Masking Guide:** How to add custom sensitive field rules
- **Performance Guide:** Index maintenance, query optimization

---

## ⏱️ Implementation Timeline (MVP)

| Phase | Tasks | Duration | Dependencies |
|-------|-------|----------|--------------|
| **FAZA 1** | Design & Architecture (this doc) | ✅ 1h | None |
| **FAZA 2** | Database schema + stored procedures | 2-3h | Phase 1 |
| **FAZA 3** | Backend (Models, Repo, Service, Masker) | 3-4h | Phase 2 |
| **FAZA 4** | Integration (Hybrid auto-capture) | 3-4h | Phase 3 |
| **FAZA 5** | Admin UI + Export | 4-5h | Phase 3, 4 |
| **FAZA 7** | Testing + Documentation | 2-3h | All phases |
| **TOTAL** | | **15-20h** | (2-3 days) |

---

## 🚀 Next Steps

### Immediate (FAZA 2 - Database)
1. ✅ Create SQL script: `013_AuditSystem.sql`
   - AuditLogs table with 6 indexes
   - AuditLogDetails table with 2 indexes
   - Foreign key constraints
   
2. ✅ Create SQL script: `014_StoredProcedures_Audit.sql`
   - 8 stored procedures for audit operations
   
3. ✅ Seed SystemParameters for audit configuration:
   - `Audit.Retention.Days` = 730 (2 years)
   - `Audit.Cleanup.Enabled` = true
   - `Audit.Cleanup.ScheduleCron` = "0 2 * * 0" (Sunday 2 AM)

4. ✅ Run migration scripts on TS1828\ERP

---

## ✅ Sign-Off

**Architecture Reviewed:** ✅ Yes  
**User Decisions Incorporated:** ✅ All 16 decisions  
**Performance Validated:** ✅ <10ms overhead acceptable  
**Security Compliant:** ✅ GDPR considerations included  
**Ready for Implementation:** ✅ YES

**Approved By:** User (2026-01-09)  
**Next Phase:** FAZA 2 - Database Schema Creation

---

**Last Updated:** 2026-01-09 10:45 AM  
**Document Version:** 1.0 (Final for MVP)
