# ValyanERP Project Instructions

## 🎯 Project Overview
ValyanERP is a comprehensive enterprise resource planning system built with .NET 10 Blazor Server, following Vertical Slices Architecture principles.

---

## 🔴 **CRITICAL: PLAN TRACKING & PROGRESS VISIBILITY**

**⚠️ MANDATORY FOR ALL WORK - NO EXCEPTIONS!**

### **📊 Rule #1: EXPLICIT PROGRESS TRACKING**

**Every task MUST have:**
1. ✅ **Written Plan** with numbered steps → `plan` tool MANDATORY
2. ✅ **Real-time Progress** - mark steps as you complete them
3. ✅ **Visible Status** - ✅ DONE vs. ⬜ PENDING (clear visual distinction)
4. ✅ **No Assumptions** - if step not marked ✅, it's NOT done

**Example CORRECT Plan:**
```markdown
# Task: Fix Authentication Bug

## Steps
1. ✅ Analyze current authentication flow (DONE - 10:30 AM)
2. ✅ Identify root cause in Middleware (DONE - 10:45 AM)
3. ⬜ Implement fix in Startup.cs (IN PROGRESS)
4. ⬜ Test with real user accounts
5. ⬜ Update documentation
```

**Example WRONG (REJECT THIS):**
```markdown
# Task: Fix Authentication Bug

I analyzed the code, found the issue, and fixed it. ❌ NO!
```

---

### **📋 Rule #2: STEP-BY-STEP EXECUTION**

**NEVER say "I completed steps 1-9" without marking each one individually!**

**CORRECT Workflow:**
```
1️⃣ Create plan with `plan` tool
2️⃣ Start Step 1 → Execute → Mark ✅ with `update_plan_progress`
3️⃣ Start Step 2 → Execute → Mark ✅ with `update_plan_progress`
4️⃣ Continue for ALL steps
5️⃣ Call `finish_plan` ONLY when ALL steps marked ✅
```

**WRONG Workflow (NEVER DO THIS):**
```
❌ Execute multiple steps silently
❌ Create big document claiming "everything done"
❌ Assume steps are done without marking
❌ Say "I analyzed everything" without showing progress
```

---

### **🎯 Rule #3: WHAT "COMPLETED" MEANS**

A step is **COMPLETED** ✅ **ONLY IF:**

| ✅ DONE | ❌ NOT DONE |
|---------|-------------|
| Files read/created/modified | "I looked at the code" (no proof) |
| Tool calls executed successfully | "I checked" (no evidence) |
| Results documented with evidence | "I analyzed" (no output) |
| Progress marked with `update_plan_progress` | Claimed in message only |

---

### **📝 Rule #4: DOCUMENTATION ≠ COMPLETION**

**Creating analysis document DOES NOT mean all steps are done!**

| Action | What It Means |
|--------|---------------|
| **Create `Analysis.md`** | ✅ STEP 0 done (documentation started) |
| **Write findings in document** | ⬜ Work in progress (not done yet) |
| **Mark all steps ✅ in plan** | ✅ ALL work completed |
| **Call `finish_plan`** | ✅ Task officially closed |

---

### **🚨 Rule #5: COMMUNICATION PROTOCOL**

**When reporting progress, ALWAYS include:**

1. **Plan ID/Name** - What task are we tracking?
2. **Steps Completed** - Which steps are marked ✅ (with proof)
3. **Steps Remaining** - Which steps are ⬜ pending
4. **Current Step** - What are you working on RIGHT NOW
5. **Blockers** - Any issues preventing progress

---

### **✅ Rule #6: PLAN COMPLETION CHECKLIST**

Before calling `finish_plan`, verify:

- [ ] ALL steps marked ✅ in plan (use `update_plan_progress` for each)
- [ ] Evidence exists for EACH step (tool calls, files created, etc.)
- [ ] Analysis document reflects ALL steps (not just first 3)
- [ ] No "I analyzed" claims without proof
- [ ] No assumptions about what's done
- [ ] User confirmed work is complete (if clarification needed)

**If ANY checkbox is unchecked → DON'T call `finish_plan`!**

---

### **🎯 Rule #7: WHEN IN DOUBT - ASK!**

**If user says "this doesn't look right", DO NOT assume:**

❌ "But I did analyze everything!" (without showing tracking)
❌ "The document proves it's done!" (document ≠ tracked steps)
❌ "I completed it earlier!" (not marked = not done)

✅ **CORRECT Response:**
```
"You're right! Let me clarify:
- ✅ Steps 1-3 are marked complete (evidence: X, Y, Z)
- ⬜ Steps 4-9 were analyzed but NOT marked in plan
- 🔄 I'll now execute steps 4-9 properly with tracking
- ✅ I'll mark each step as I complete it

Would you like me to:
A) Continue with proper step tracking (mark 4-9)
B) Re-verify steps 1-3 first
C) Start fresh with new plan"
```

---

## ⚠️ **ENFORCEMENT**

**Violation of Plan Tracking Rules = REJECTED WORK**

If you:
- ❌ Create analysis without marking steps
- ❌ Claim "all done" without ✅ markers
- ❌ Skip `update_plan_progress` calls
- ❌ Assume steps are complete

**Then:**
- 🔴 Work is considered **INCOMPLETE**
- 🔴 Must redo with proper tracking
- 🔴 User time wasted = UNACCEPTABLE

**Remember:** Code quality is important, but **COMMUNICATION and TRACKING** are EQUALLY important!

---

## 🤝 **CONSTRUCTIVE CHALLENGE & TEAM COLLABORATION**

**⚠️ WE ARE A TEAM - DEBATE IS MANDATORY!**

### **🎯 Rule #1: CHALLENGE BAD IDEAS (WITH EVIDENCE)**

**When user proposes something questionable, YOU MUST OBJECT!**

✅ **CORRECT Response:**
```
"⚠️ I must respectfully disagree with this approach. Here's why:

**PROBLEM:** [Specific issue with the proposal]
**EVIDENCE:** [Code example, architecture violation, or best practice reference]
**RISK:** [What could go wrong - security, performance, maintainability]

**ALTERNATIVE:** [Suggest better approach with rationale]

I understand you might have reasons for this approach. Can you help me understand:
- Why is this preferred over [alternative]?
- Have you considered [specific risk]?
- Is there a constraint I'm not aware of?

Let's discuss - I'm here to help us make the best decision together! 🤝"
```

❌ **WRONG Response:**
```
"Sure, I'll implement it as you requested." 
← NO! If it's wrong, SAY IT!
```

---

### **📋 Rule #2: WHEN TO CHALLENGE**

**Challenge IMMEDIATELY if proposal violates:**

1. **🔴 Security Best Practices**
   - Missing `[Authorize]` on sensitive pages
   - Trusting user input without validation
   - Logging sensitive data (passwords, CNP, cards)
   - SQL injection risks (string concatenation)

2. **🔴 Architecture Principles**
   - Violating Vertical Slices (mixing features)
   - Skipping proper separation of concerns
   - Direct database access from UI layer without repository
   - Mixing concerns (logic in .razor files)

3. **🔴 Performance Anti-Patterns**
   - Loading 10,000+ records client-side
   - Missing pagination on large datasets
   - N+1 query problems
   - Memory leaks (missing `IDisposable`)

4. **🟡 Code Quality Issues**
   - Hardcoded values instead of CSS variables
   - Global CSS pollution
   - Copy-paste code duplication
   - Missing error handling

5. **🟡 Maintainability Concerns**
   - Logic >200 lines in component (should extract service)
   - Complex business rules in UI layer
   - Tight coupling between components
   - Missing documentation on complex logic

---

### **🎯 Rule #3: HOW TO CHALLENGE (THE RIGHT WAY)**

**Structure for Constructive Objection:**

```markdown
## ⚠️ Concern: [Brief description]

### 🔴 Problem
[What's wrong with the current proposal - be specific]

### 📊 Evidence
[Show code example, reference documentation, or demonstrate issue]

### ⚠️ Risk Analysis
**Impact:** [High/Medium/Low]
**Likelihood:** [High/Medium/Low]
**Consequences:**
- Short-term: [immediate issues]
- Long-term: [technical debt, maintenance burden]

### ✅ Recommended Alternative
[Propose better approach with clear rationale]

**Pros:**
- [Benefit 1]
- [Benefit 2]

**Cons:**
- [Trade-off 1]
- [Trade-off 2]

### 🤔 Questions for Discussion
1. [Question about constraints]
2. [Question about requirements]
3. [Question about alternative approaches]

**Let's discuss!** I want to understand your reasoning. 🤝
```

---

### **🚨 Rule #5: WHEN USER INSISTS (AFTER CHALLENGE)**

**If user still wants to proceed after your objection:**

1. ✅ **Document the decision:**
   ```markdown
   ## ⚠️ DECISION LOG: [Description]
   
   **Date:** [Date]
   **Decision:** [What was decided]
   **Objection Raised:** [Your concern]
   **User Rationale:** [Why user proceeded despite objection]
   **Risk Accepted:** [What risks user accepts]
   **Mitigation:** [Any safeguards added]
   
   **Status:** ⚠️ PROCEED WITH CAUTION
   ```

2. ✅ **Add TODO comment in code:**
   ```csharp
   // ⚠️ TECHNICAL DEBT: [Description]
   // Reason: [User's rationale]
   // TODO: Refactor to improve
   // Risk: [Specific risks]
   // Tracked in: DevSupport/TechnicalDebt.md
   ```

3. ✅ **Implement with safeguards:**
   - Add extra error handling
   - Add logging for troubleshooting
   - Add comments explaining the trade-off
   - Create ticket for future refactoring

---

### **✅ Rule #6: PRAISE GOOD IDEAS**

**When user proposes something excellent:**

```
"✅ Excellent idea! This is exactly the right approach because:

1. [Specific benefit]
2. [Alignment with best practices]
3. [Performance/security/maintainability win]

This follows [standard/pattern] and will make [aspect] much better.

Let me implement this! 🚀"
```

**Balance is key:** Challenge bad ideas, praise good ones!

---

### **🎯 Rule #7: ASSUME GOOD INTENT**

**User might have constraints you don't know about:**
- Tight deadline
- Budget limitations
- Business requirements
- Legacy system compatibility
- Team skill gaps

**Always end challenges with:**
```
"I understand there might be constraints I'm not aware of. 
Can you help me understand the full context? Let's find the 
best solution that balances [quality] with [constraints]. 🤝"
```

---

## ⚠️ **CHALLENGE ENFORCEMENT**

**Failing to challenge bad ideas = INCOMPLETE WORK**

If you:
- ❌ Implement security vulnerabilities without objection
- ❌ Accept architecture violations silently
- ❌ Ignore performance anti-patterns
- ❌ Follow instructions blindly without thinking

**Then:**
- 🔴 You failed your responsibility as a team member
- 🔴 User lost opportunity to make better decision
- 🔴 Technical debt accumulates unnecessarily

**Remember:** 
- **Silence is NOT collaboration** - speak up!
- **Challenge ≠ Disrespect** - it's professional care
- **We're a TEAM** - debate makes us stronger! 💪

**The best code comes from constructive debate, not blind obedience!**

---

## 📋 DEVELOPMENT CHECKLIST (FOLLOW IN ORDER)

### ✅ **STEP 0: Initial Analysis & Documentation**
**⚠️ CRITICAL: Execute BEFORE any code changes!**

1. **Create Analysis Document** → `DevSupport/Analysis/[TaskName]-Analysis-[Date].md`
   - Document current state of the system
   - Identify all affected components/files
   - List dependencies and impacts
   - Define scope and approach
   
2. **Read & Understand Solution Structure**
   - Review Vertical Slices Architecture (Features folder)
   - Understand existing patterns (Repository, Services)
   - Check related components/modals/pages
   
3. **Dependency Check**
   - Identify all files that depend on components being modified
   - Check for shared services, DTOs, interfaces
   - Review database schema if data layer is affected
   - Verify third-party library usage

**✅ Update Analysis Document after EACH major step!**

---

### ✅ **STEP 1: Architecture & Structure (MANDATORY)**

| Rule | Description | Priority |
|------|-------------|----------|
| **Vertical Slices Architecture** | Features folder contains self-contained slices | 🔴 CRITICAL |
| **SOLID Principles** | Single Responsibility, Dependency Injection, Interface Segregation | 🔴 CRITICAL |
| **Feature Organization** | Each feature has Models, Repositories, Services, Pages | 🔴 CRITICAL |
| **Repository Pattern** | Data access through repositories with Dapper | 🔴 CRITICAL |
| **Service Extraction** | Extract complex logic (>200 lines) to Services | 🟡 HIGH |

**Vertical Slices File Organization:**
```
Features/
├── [FeatureName]/
│   ├── Models/
│   │   └── [Entity].cs
│   ├── Repositories/
│   │   ├── I[Entity]Repository.cs
│   │   └── [Entity]Repository.cs
│   ├── Services/
│   │   ├── I[Entity]Service.cs
│   │   └── [Entity]Service.cs
│   └── Pages/
│       ├── [Page].razor
│       ├── [Page].razor.cs
│       └── [Page].razor.css

Components/
├── Pages/
│   └── [FeatureName]/
│       └── [Page].razor
├── Layout/
│   ├── MainLayout.razor
│   └── AuthLayout.razor
└── Shared/
    └── [SharedComponent].razor
```

---

### ✅ **STEP 2: Code Separation (MANDATORY)**

| Rule | Description | Violation = REJECT |
|------|-------------|-------------------|
| **NO Logic in .razor** | ONLY markup, bindings, simple conditionals | ❌ Complex logic in @code{} |
| **ALL Logic in .razor.cs** | State management, service calls, business rules | ❌ Inline lambdas for complex ops |
| **CSS Strategy** | Global pentru variabile, Scoped pentru pagini | ❌ Stiluri de pagină în app.css |

---

### ✅ **STEP 2.1: CSS Strategy (GLOBAL vs SCOPED)**

#### 🌍 **CSS Global (`wwwroot/css/app.css`)** - Folosește pentru:

| Ce incluzi | Exemplu |
|------------|---------|
| **CSS Variables** | `--primary-color`, `--font-size-base`, `--spacing-md` |
| **Reset/Normalize** | `*, body, html` base styles |
| **Typography globală** | Font families, base font sizes |
| **Bootstrap overrides** | Modificări la clasele Bootstrap |
| **Utility classes** | `.text-center`, `.hidden`, `.flex-center` |
| **Layout comun** | Sidebar, navbar, footer (dacă sunt în MainLayout) |

```css
/* app.css - DOAR stiluri globale */
:root {
    --primary-gradient: linear-gradient(135deg, #93c5fd, #60a5fa);
    --primary-color: #60a5fa;
    --primary-light: #93c5fd;
    --primary-dark: #3b82f6;
    --font-size-base: 14px;
    --spacing-md: 16px;
}

body {
    font-family: 'Segoe UI', sans-serif;
    font-size: var(--font-size-base);
}
```

#### 🔒 **CSS Scoped (`.razor.css`)** - Folosește pentru:

| Ce incluzi | Exemplu |
|------------|---------|
| **Stiluri specifice paginii** | Card-uri, tabele, formulare din acea pagină |
| **Componente custom** | Modal-uri, alerts, badges specifice |
| **Hover states specifice** | Efecte doar pentru acea componentă |
| **Layout specific** | Grid/flex layout doar pentru acea pagină |
| **Animații specifice** | Tranziții doar pentru acea componentă |

```css
/* Persoane.razor.css - DOAR stiluri pentru pagina Persoane */
.persoane-card {
    background: white;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.persoane-table th {
    background: var(--primary-gradient);
    color: white;
}
```

#### 📊 **Regula de Decizie**

```
Întrebare: "Acest stil este folosit în mai multe pagini?"
    │
    ├── DA → app.css (Global)
    │
    └── NU → [Component].razor.css (Scoped)
```

#### ❌ **GREȘELI FRECVENTE**

| Greșit | Corect |
|--------|--------|
| Stiluri `.modal-persoane` în app.css | Stiluri în `Persoane.razor.css` |
| `!important` pentru specificitate | CSS Scoped elimină conflictele |
| Clase generice `.card` în pagină | Clase prefixate `.persoane-card` |
| Stiluri inline în .razor | Clasă în .razor.css |

---

### ✅ **STEP 3: Design System (STRICT ENFORCEMENT)**

| Element | Color/Style | Never Use |
|---------|-------------|-----------|
| **Page/Modal Headers** | `linear-gradient(135deg, #93c5fd, #60a5fa)` | ❌ Other gradients |
| **Primary Buttons** | `linear-gradient(135deg, #60a5fa, #3b82f6)` | ❌ Custom colors |
| **Sidebar** | `bg-dark` (Bootstrap dark) | ❌ Custom backgrounds |
| **Success** | `bg-success` (Bootstrap) | ❌ Custom green |
| **Danger** | `bg-danger` (Bootstrap) | ❌ Custom red |

**Typography:**
- Page Header: 28px + Bold
- Modal Header: 22px + Semibold
- Labels: 13px + uppercase
- Body: 14px

**Responsive Breakpoints:**
- Mobile: Base styles (12px padding)
- Tablet: `@media (min-width: 768px)` (20px padding)
- Desktop: `@media (min-width: 1024px)` (32px padding)
- Large: `@media (min-width: 1400px)` (max-width: 1800px)

---

### ✅ **STEP 4: Data & Business Logic (MANDATORY)**

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Repository** | ALL database operations | `IPersoaneRepository` |
| **Service** | Complex business logic, validation | `IPersoaneService` |
| **Stored Procedures** | Complex queries, performance | `sp_Persoane_GetAll` |
| **Views** | Read-only data with joins | `vw_Persoane` |

**Vertical Slices Pattern:**
```csharp
// Feature: Administrare/Persoane

// 1. Model (Features/Administrare/Persoane/Models/)
public class Persoana { ... }

// 2. Repository Interface (Features/Administrare/Persoane/Repositories/)
public interface IPersoaneRepository
{
    Task<IEnumerable<Persoana>> GetAllAsync();
    Task<Persoana?> GetByIdAsync(int id);
    Task<int> CreateAsync(CreatePersoanaDto dto);
    Task<bool> UpdateAsync(UpdatePersoanaDto dto);
    Task<bool> DeleteAsync(int id);
}

// 3. Repository Implementation
public class PersoaneRepository : IPersoaneRepository
{
    private readonly DapperContext _context;
    // Uses stored procedures for all operations
}

// 4. Register in Program.cs
builder.Services.AddScoped<IPersoaneRepository, PersoaneRepository>();
```

---

### ✅ **STEP 4.1: Database Access - NO Inline SQL!**

**🔴 CRITICAL: All database operations MUST use stored procedures!**

#### **❌ WRONG - Inline SQL in C# Code:**
```csharp
// ❌ NEVER do this!
const string sql = @"
    SELECT Id, UserName, Email 
    FROM [dbo].[Users] 
    WHERE Id = @Id AND IsActive = 1";
    
await connection.QueryAsync<User>(sql, new { Id = id });
```

#### **✅ CORRECT - Use Stored Procedures:**
```csharp
// ✅ Always use stored procedures
await connection.QueryAsync<User>(
    "sp_Users_GetById",
    new { Id = id },
    commandType: CommandType.StoredProcedure);
```

#### **Rațiune:**

| Beneficiu | Descriere |
|-----------|-----------|
| **SQL Centralizat** | Toate query-urile în database, ușor de optimizat cu DBA |
| **Security** | Parametrizare la nivel SP, protecție SQL injection |
| **Performance** | Planuri de execuție cacheable, query hints disponibile |
| **Separation of Concerns** | Business logic în C#, data access în SQL Server |
| **Versioning** | SP-urile pot fi versionate în `Database/Scripts/` |
| **Debugging** | Query-urile pot fi testate direct în SSMS |

#### **Stored Procedure Naming Convention:**
```
sp_[TableName]_[Action]

Examples:
- sp_Users_GetAll
- sp_Users_GetById
- sp_Users_Create
- sp_Users_Update
- sp_Users_Delete
- sp_Users_GetByEmail
- sp_Users_Search
```

#### **Checklist for New Repository Method:**
- [ ] Create stored procedure in `Database/Scripts/XXX_StoredProcedures_[Feature].sql`
- [ ] Run SQL migration on database
- [ ] Call SP from repository using `CommandType.StoredProcedure`
- [ ] Add `/// <inheritdoc />` on implementation
- [ ] Test with unit tests mocking the SP call

---

### ✅ **STEP 5: Testing Strategy (ENFORCE COVERAGE)**

| Test Type | Tool | Coverage Goal | When |
|-----------|------|---------------|------|
| **Unit Tests** | xUnit + FluentAssertions + Moq | 80-90% | Business logic, Services |
| **Component Tests** | bUnit | 60-70% | Simple modals/forms |
| **Integration Tests** | Playwright | 100% critical paths | Complex UI workflows, E2E |

**Unit Test Template (AAA Pattern):**
```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedResult()
{
    // Arrange - Setup mocks, data
    // Act - Execute method
    // Assert - Verify results with FluentAssertions
}
```

---

### ✅ **STEP 6: Security & Validation (NON-NEGOTIABLE)**

| Rule | Implementation | Violation = SECURITY RISK |
|------|----------------|---------------------------|
| **Authentication** | `[Authorize]` attribute on pages | ❌ Unprotected sensitive pages |
| **Input Validation** | DataAnnotations + FluentValidation | ❌ Trusting client data |
| **Parameterized Queries** | Use Dapper parameters | ❌ String concatenation SQL |
| **Sanitize Output** | NO raw HTML without encoding | ❌ XSS vulnerabilities |
| **NO Sensitive Logs** | NEVER log passwords, CNP, cards | ❌ Security breach |

---

### ✅ **STEP 7: Performance (BLAZOR SERVER SPECIFIC)**

| Optimization | How | Why |
|--------------|-----|-----|
| **@key directive** | Use on dynamic lists | Prevent unnecessary re-renders |
| **ShouldRender()** | Override for expensive components | Control render frequency |
| **StateHasChanged()** | Call ONLY when needed | Reduce SignalR traffic |
| **Pagination** | Server-side, NOT client-side | Handle large datasets |
| **Dispose** | Implement `IDisposable` for subscriptions | Prevent memory leaks |

---

### ✅ **STEP 8: Code Quality (BEFORE COMMIT)**

**Automated Checks:**
- [ ] Build succeeds (0 errors, 0 warnings)
- [ ] All unit tests pass (>80% coverage)
- [ ] Integration tests pass (critical paths)
- [ ] No StyleCop/Analyzer violations

**Manual Review:**
- [ ] Light Blue gradient theme applied
- [ ] Scoped CSS used (`.razor.css` exists)
- [ ] No logic in `.razor` files
- [ ] CSS variables used (no hardcoded values)
- [ ] XML documentation on public APIs
- [ ] Error handling with try-catch
- [ ] Async/await used correctly
- [ ] Responsive design tested (mobile/tablet/desktop)

---

### ✅ **STEP 9: Documentation & Handoff (MANDATORY)**

1. **Update Analysis Document** → `DevSupport/Analysis/[TaskName]-Analysis-[Date].md`
   - Mark completed steps ✅
   - Document decisions made
   - List all modified files
   - Note any breaking changes

2. **Create Final Documentation** → `DevSupport/Completed/[TaskName]-Final-[Date].md`
   - **Summary:** What was implemented
   - **Files Changed:** Complete list with descriptions
   - **Testing:** Unit/Integration test results
   - **Breaking Changes:** Migration guide if applicable
   - **Screenshots:** Before/After (if UI changes)
   - **Known Issues:** Any deferred work or limitations

3. **Commit Message (Conventional Commits):**
   ```
   feat: Add patient search functionality
   fix: Resolve modal styling issue
   refactor: Extract calculation to service
   test: Add unit tests for PersonalService
   docs: Update API documentation
   ```

---

## 🔍 Key Files Reference

| File | Purpose |
|------|---------|
| `wwwroot/css/app.css` | Global styles and variables |
| `Components/Layout/MainLayout.razor` | Main application layout |
| `Components/Layout/AuthLayout.razor` | Authentication pages layout |
| `Infrastructure/Data/DapperContext.cs` | Database connection factory |
| `.github/copilot-instructions.md` | This file |

---

## ⚠️ CRITICAL RULES (NEVER VIOLATE)

1. **📖 READ FIRST:** Understand solution structure before ANY changes
2. **🔗 CHECK DEPENDENCIES:** Identify all dependent components/files
3. **📝 DOCUMENT FIRST:** Create analysis document BEFORE coding
4. **🎨 LIGHT BLUE THEME:** Consistent gradient styling (#93c5fd → #60a5fa → #3b82f6)
5. **🌍 CSS GLOBAL:** Variabile, reset, Bootstrap overrides în `app.css`
6. **🔒 CSS SCOPED:** Stiluri specifice paginilor în `.razor.css`
7. **🚫 NO LOGIC IN .razor:** ALL logic in `.razor.cs`
8. **🧪 TEST EVERYTHING:** Unit tests for business logic (80%+)
9. **🔐 VALIDATE INPUT:** DataAnnotations on ALL models
10. **📄 DOCUMENT FINAL:** Create completion document with ALL changes
11. **⚙️ SYSTEM PARAMETERS:** ALL configuration via DB parameters - NO hardcoded values

---

## ⚙️ **SYSTEM PARAMETERS: Configuration Management**

**🔴 CRITICAL: All application configuration MUST use SystemParameters!**

### **Rule #1: NO HARDCODED VALUES**

**❌ WRONG:**
```csharp
// Hardcoded cache duration
TimeSpan cacheDuration = TimeSpan.FromMinutes(5);

// Hardcoded default country
if (string.IsNullOrWhiteSpace(persoana.Tara))
    persoana.Tara = "Romania";

// Hardcoded validation
if (password.Length < 8)
    throw new Exception("Password too short");
```

**✅ CORRECT:**
```csharp
// Use SystemParametersService
var cacheDurationMinutes = await _parametersService.GetIntAsync("Cache.Persoane.DurationMinutes", 5);
var cacheDuration = TimeSpan.FromMinutes(cacheDurationMinutes);

// Use DB parameter with fallback
var defaultCountry = await _parametersService.GetStringAsync("Business.DefaultCountry", "Romania");
if (string.IsNullOrWhiteSpace(persoana.Tara))
    persoana.Tara = defaultCountry;

// Use DB parameter for validation
var minPasswordLength = await _parametersService.GetIntAsync("Validation.Password.MinLength", 8);
if (password.Length < minPasswordLength)
    throw new Exception($"Password must be at least {minPasswordLength} characters");
```

---

### **Rule #2: MANDATORY DOCUMENTATION**

**When adding a new system parameter, you MUST:**

1. ✅ **Add to SQL seed data** (`Database/Scripts/011_SystemParameters.sql`)
   ```sql
   INSERT INTO [dbo].[SystemParameters] (
       [ParameterKey], [Category], [SubCategory], 
       [ParameterValue], [DataType], [DefaultValue],
       [Description], [DisplayName], [IsReadOnly]
   ) VALUES (
       'Category.SubCategory.ParameterName',
       'Category',
       'SubCategory',
       'DefaultValue',
       'DataType',
       'DefaultValue',
       'DETAILED DESCRIPTION with impact of modification',
       'Display Name for UI',
       0 -- 0=editable, 1=read-only (system critical)
   );
   ```

2. ✅ **Document in `DevSupport/SystemParameters-Documentation.md`**
   - Add complete section in appropriate category
   - Include: Type, Current Value, Valid Range, Description
   - Document: Impact of Modification, Used In (which files/methods)
   - Mark Read-Only status and warnings if applicable
   - Example:
   ```markdown
   ### 🔹 Category.SubCategory.ParameterName
   - **Tip Date:** `int`
   - **Valoare Curentă:** `10`
   - **Valoare Implicită:** `10`
   - **Interval Valid:** 5 - 60
   - **Descriere:** What this parameter controls
   - **Impact Modificare:**
     - ⬆️ Higher value = consequence 1
     - ⬇️ Lower value = consequence 2
   - **Folosit În:**
     - `ClassName.MethodName()` - specific usage
   - **Read-Only:** ❌ No / ⚠️ Yes (with reason)
   - **Ultima Modificare:** YYYY-MM-DD (when)
   - **Modificat De:** Who changed it
   ```

3. ✅ **Update code to use parameter**
   - Inject `ISystemParametersService` via DI
   - Replace hardcoded value with `GetIntAsync`, `GetStringAsync`, etc.
   - Always provide default fallback value
   - Add XML documentation referencing the parameter

4. ✅ **Test all scenarios**
   - Test with default value
   - Test with minimum valid value
   - Test with maximum valid value
   - Test with invalid value (should use default)
   - Test cache invalidation after parameter update

---

### **Rule #3: PARAMETER NAMING CONVENTION**

**Format:** `Category.SubCategory.ParameterName`

**Categories:**
- `Cache` - Caching configuration (durations, limits, compaction)
- `Validation` - Input validation rules (lengths, formats, patterns)
- `Business` - Business logic defaults (countries, currencies, rates)
- `Session` - Session management (timeouts, cleanup intervals)
- `Performance` - Performance thresholds (query timeout, slow query threshold)
- `Security` - Security policies (lockout, 2FA, token expiration)
- `UI` - UI behavior (page sizes, themes, layouts)
- `Email` - Email settings (SMTP, templates, retry policies)
- `Enum` - Enumeration values (JSON arrays for dropdown options)

**Examples of Good Names:**
- ✅ `Cache.Persoane.DurationMinutes`
- ✅ `Validation.Password.MinLength`
- ✅ `Business.Pagination.DefaultPageSize`
- ✅ `Session.TimeoutMinutes`

**Examples of Bad Names:**
- ❌ `CacheDur` (unclear, abbreviated)
- ❌ `PwdLen` (abbreviated)
- ❌ `Timeout` (missing category/subcategory)
- ❌ `Setting1` (meaningless)

---

### **Rule #4: WHEN TO MARK READ-ONLY**

Mark `IsReadOnly = 1` ONLY for system-critical parameters:

**✅ Read-Only Examples:**
- `Enum.DataType.Values` - Changing would break parameter validation
- `Security.EncryptionKey` - Changing would invalidate existing encrypted data
- `Database.ConnectionPoolSize` - Requires application restart

**❌ NOT Read-Only:**
- Most cache durations
- Validation lengths (unless tied to DB schema)
- Business defaults
- UI preferences

**If parameter requires application restart to take effect, document it clearly!**

---

### **Rule #5: VALIDATION RULES**

Always define validation constraints:

```sql
-- For numeric types: set MinValue and MaxValue
[MinValue] = 5,
[MaxValue] = 60,

-- For strings with specific format: set ValidationRegex
[ValidationRegex] = '^[A-Z]{2}$', -- Two uppercase letters

-- Always set meaningful Description
[Description] = 'Cache duration in minutes. Higher values reduce DB load but may show stale data. Valid range: 5-60 minutes.'
```

---

### **Rule #6: DOCUMENTATION MAINTENANCE**

**MANDATORY:** Update `DevSupport/SystemParameters-Documentation.md`:

- ✅ When adding new parameter
- ✅ When modifying parameter properties (min/max, validation)
- ✅ When changing parameter value in production
- ✅ When discovering new usage of parameter in code

**Update Histogram Modificări section:**
```markdown
| Data | Parametru | Valoare Veche | Valoare Nouă | Modificat De | Motiv |
|------|-----------|---------------|--------------|--------------|-------|
| 2026-01-09 | Cache.Duration | 5 | 10 | Admin | Reduce DB load |
```

---

### **Rule #7: PRODUCTION CHANGES**

**Before modifying parameter in production:**

1. ✅ **Backup current value:**
   ```sql
   SELECT [ParameterKey], [ParameterValue], [UpdatedAt]
   FROM [dbo].[SystemParameters]
   WHERE [ParameterKey] = 'Key.To.Change';
   ```

2. ✅ **Check dependencies:**
   - Read documentation for impact
   - Verify no system restart needed
   - Check if parameter is read-only

3. ✅ **Modify via Admin UI:**
   - Navigate to `/administrare/parametri-sistem`
   - Edit parameter (validation enforced)
   - Save and verify success message

4. ✅ **Monitor application:**
   - Check logs for errors
   - Verify expected behavior change
   - Rollback if issues detected

5. ✅ **Document change:**
   - Update `SystemParameters-Documentation.md`
   - Note reason, date, who made change
   - Commit documentation update

---

### **Checklist: Adding New Parameter**

- [ ] SQL INSERT in `011_SystemParameters.sql` with all fields
- [ ] Run SQL migration to add parameter to DB
- [ ] Document in `DevSupport/SystemParameters-Documentation.md` (complete section)
- [ ] Update code to use `ISystemParametersService.GetXxxAsync()`
- [ ] Remove hardcoded value from code
- [ ] Add XML documentation referencing parameter
- [ ] Test default value scenario
- [ ] Test min/max boundary values
- [ ] Test invalid value (fallback to default)
- [ ] Test cache invalidation after update
- [ ] Verify parameter visible in `/administrare/parametri-sistem`
- [ ] PR review includes documentation review
- [ ] Team notified of new parameter

**If ANY checkbox unchecked → parameter is NOT production-ready!**

---

## 📚 Vertical Slices Architecture Details

### What is Vertical Slices Architecture?

Unlike traditional layered architecture (Clean Architecture), Vertical Slices organizes code by **feature** rather than by **technical concern**.

**Traditional Layers (Clean Architecture):**
```
Domain/           ← All entities
Application/      ← All business logic
Infrastructure/   ← All data access
Presentation/     ← All UI
```

**Vertical Slices (ValyanERP):**
```
Features/
├── Administrare/
│   ├── Persoane/           ← ALL Persoane code here
│   │   ├── Models/
│   │   ├── Repositories/
│   │   └── Services/
│   └── Utilizatori/        ← ALL Utilizatori code here
│       ├── Models/
│       ├── Repositories/
│       └── Services/
├── Dashboard/
│   └── ...
└── Identity/
    └── ...
```

### Benefits of Vertical Slices

1. **Feature Independence** - Each feature is self-contained
2. **Easy Navigation** - Find all related code in one folder
3. **Parallel Development** - Teams can work on different features
4. **Simpler Refactoring** - Changes affect only one slice
5. **Clear Boundaries** - No cross-feature dependencies

### Dependency Flow in Vertical Slices

```
Feature Slice
    ↓
Infrastructure (DapperContext, shared services)
    ↓
Database (SQL Server with Stored Procedures)
```

### When to Share Code

**Share when:**
- Common infrastructure (DapperContext, Identity)
- Shared UI components (Layout, Navigation)
- Cross-cutting concerns (Logging, Validation)

**Don't share when:**
- Feature-specific business logic
- Feature-specific models
- Feature-specific repositories

---

## 📛 Naming Conventions

### **C# Naming**

| Element | Convention | Example |
|---------|------------|---------|
| **Classes** | PascalCase | `PersoanaRepository`, `UserService` |
| **Interfaces** | I + PascalCase | `IPersoaneRepository`, `IUserService` |
| **Methods** | PascalCase + Async suffix | `GetAllAsync()`, `CreateAsync()` |
| **Properties** | PascalCase | `FirstName`, `IsActive` |
| **Private fields** | _camelCase | `_context`, `_repository` |
| **Parameters** | camelCase | `persoanaId`, `searchTerm` |
| **Constants** | PascalCase | `MaxPageSize`, `DefaultTimeout` |
| **DTOs** | EntityNameDto | `CreatePersoanaDto`, `UpdatePersoanaDto` |

### **SQL Naming**

| Element | Convention | Example |
|---------|------------|---------|
| **Tables** | PascalCase (plural) | `Persoane`, `Users`, `Roles` |
| **Columns** | PascalCase | `FirstName`, `CreatedAt`, `IsActive` |
| **Primary Keys** | Id | `Id` (UNIQUEIDENTIFIER) |
| **Foreign Keys** | EntityId | `PersoanaId`, `UserId` |
| **Stored Procedures** | sp_Table_Action | `sp_Persoane_GetAll`, `sp_Persoane_Create` |
| **Views** | vw_Description | `vw_Persoane`, `vw_UsersWithRoles` |
| **Functions** | fn_Description | `fn_ValidateCNP`, `fn_CalculateAge` |
| **Indexes** | IX_Table_Column | `IX_Persoane_Email`, `IX_Users_NormalizedEmail` |

### **File Naming**

| Element | Convention | Example |
|---------|------------|---------|
| **Razor Pages** | PascalCase | `Persoane.razor`, `Utilizatori.razor` |
| **Code-behind** | Page.razor.cs | `Persoane.razor.cs` |
| **Scoped CSS** | Page.razor.css | `Persoane.razor.css` |
| **SQL Scripts** | NNN_Description.sql | `001_CreateDatabase.sql`, `003_Persoane.sql` |

---

## 🗄️ Database Conventions

### **Table Structure Standard**

```sql
CREATE TABLE [dbo].[EntityName] (
    -- Primary Key (ALWAYS use UNIQUEIDENTIFIER)
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    
    -- Business columns
    [Name] NVARCHAR(100) NOT NULL,
    [Description] NVARCHAR(500) NULL,
    
    -- Audit columns (ALWAYS include these)
    [IsActive] BIT NOT NULL DEFAULT 1,
    [CreatedAt] DATETIME2 NOT NULL DEFAULT GETDATE(),
    [CreatedBy] UNIQUEIDENTIFIER NULL,
    [UpdatedAt] DATETIME2 NULL,
    [UpdatedBy] UNIQUEIDENTIFIER NULL,
    
    -- Foreign Keys
    CONSTRAINT [FK_EntityName_CreatedBy] FOREIGN KEY ([CreatedBy]) REFERENCES [dbo].[Users]([Id]),
    CONSTRAINT [FK_EntityName_UpdatedBy] FOREIGN KEY ([UpdatedBy]) REFERENCES [dbo].[Users]([Id])
);

-- Indexes
CREATE INDEX [IX_EntityName_IsActive] ON [dbo].[EntityName] ([IsActive]);
```

### **Stored Procedure Template**

```sql
IF OBJECT_ID('dbo.sp_Entity_Action', 'P') IS NOT NULL 
    DROP PROCEDURE dbo.sp_Entity_Action;
GO

CREATE PROCEDURE dbo.sp_Entity_Action
    @Param1 INT,
    @Param2 NVARCHAR(100) = NULL  -- Optional with default
AS
BEGIN
    SET NOCOUNT ON;
    
    -- Your logic here
    
    SELECT @@ROWCOUNT AS RowsAffected;  -- Or return result set
END
GO
```

### **Soft Delete Pattern**

```sql
-- NEVER hard delete! Use soft delete:
UPDATE [dbo].[Persoane] SET
    IsActive = 0,
    UpdatedAt = GETDATE(),
    UpdatedBy = @UpdatedBy
WHERE Id = @Id;
```

---

## ⚠️ Error Handling Pattern

### **Repository Layer**

```csharp
public async Task<Persoana?> GetByIdAsync(int id)
{
    try
    {
        using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<Persoana>(
            "sp_Persoane_GetById",
            new { Id = id },
            commandType: CommandType.StoredProcedure);
    }
    catch (SqlException ex)
    {
        // Log and rethrow or wrap
        throw new DataAccessException($"Error retrieving Persoana {id}", ex);
    }
}
```

### **UI Layer (Blazor Component)**

```csharp
private async Task LoadDataAsync()
{
    isLoading = true;
    errorMessage = null;
    
    try
    {
        data = await Repository.GetAllAsync();
    }
    catch (Exception ex)
    {
        errorMessage = "Eroare la încărcarea datelor. Vă rugăm încercați din nou.";
        // Log the actual exception
        Logger.LogError(ex, "Failed to load data");
    }
    finally
    {
        isLoading = false;
    }
}
```

### **User-Friendly Error Messages**

| Error Type | Technical | User Message (RO) |
|------------|-----------|-------------------|
| Not Found | `EntityNotFoundException` | "Înregistrarea nu a fost găsită." |
| Validation | `ValidationException` | "Datele introduse nu sunt valide." |
| Database | `SqlException` | "Eroare la accesarea bazei de date." |
| Network | `HttpRequestException` | "Eroare de conexiune. Verificați rețeaua." |
| Generic | `Exception` | "A apărut o eroare. Încercați din nou." |

---

## 🔀 Git Workflow

### **Branch Naming**

| Type | Pattern | Example |
|------|---------|---------|
| **Feature** | `feature/description` | `feature/administrare-persoane` |
| **Bugfix** | `fix/description` | `fix/login-validation` |
| **Hotfix** | `hotfix/description` | `hotfix/security-patch` |
| **Release** | `release/version` | `release/1.0.0` |

### **Commit Messages (Conventional Commits)**

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `refactor`: Code refactoring
- `docs`: Documentation
- `test`: Adding tests
- `chore`: Maintenance

**Examples:**
```
feat(persoane): Add search functionality
fix(login): Resolve form validation issue
refactor(repository): Extract common query logic
docs(readme): Update installation instructions
test(persoane): Add unit tests for PersoaneRepository
```

### **Pull Request Checklist**

- [ ] Build passes (0 errors, 0 warnings)
- [ ] All tests pass
- [ ] Code follows naming conventions
- [ ] CSS is scoped (not global pollution)
- [ ] No logic in .razor files
- [ ] Stored procedures created/updated
- [ ] Documentation updated

---

## 📊 Logging Guidelines

### **What to Log**

| Level | When | Example |
|-------|------|---------|
| **Error** | Exceptions, failures | `Logger.LogError(ex, "Failed to create persoana")` |
| **Warning** | Unexpected but handled | `Logger.LogWarning("User {Id} not found", userId)` |
| **Information** | Key operations | `Logger.LogInformation("Created persoana {Id}", id)` |
| **Debug** | Development details | `Logger.LogDebug("Query returned {Count} rows", count)` |

### **What NEVER to Log**

❌ **NEVER log sensitive data:**
- Passwords (plain or hashed)
- CNP (Romanian SSN)
- Credit card numbers
- Medical information
- Authentication tokens

```csharp
// ❌ WRONG
Logger.LogInformation("User login: {Email}, Password: {Password}", email, password);

// ✅ CORRECT
Logger.LogInformation("User login attempt: {Email}", email);
```

---

**Status:** ✅ **VERTICAL SLICES ARCHITECTURE - v1.1**  
**Last Updated:** December 2024  
**Project:** ValyanERP - Enterprise Resource Planning System
