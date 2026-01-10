# Analysis: Server-side Sessions (Option B)

## Goal
Implement server-side session management so that:
- Closing the browser invalidates the user's session so re-opening requires login.
- Losing connection invalidates session promptly so reconnection requires login.
- Sessions can be invalidated immediately (on circuit close or explicit client unload).

## Design Overview
1. DB: Create `Sessions` table with columns:
   - Id UNIQUEIDENTIFIER PK
   - UserId UNIQUEIDENTIFIER
   - Token UNIQUEIDENTIFIER (session token stored in cookie)
   - CircuitId NVARCHAR(200) NULL
   - CreatedAt DATETIME2
   - LastHeartbeat DATETIME2
   - IsActive BIT
   - ExpiresAt DATETIME2 NULL
   - Metadata JSON (optional)

2. Flow:
   - On successful login: create session row, set session cookie (session cookie, not persistent unless RememberMe).
   - Middleware validates cookie token and ensures session IsActive and not expired on each request.
   - On logout: mark session IsActive = 0 and remove cookie.
   - On circuit disconnect or JS beforeunload: call invalidate endpoint marking session inactive immediately.
   - Heartbeat endpoint (optional) to update LastHeartbeat while page is active.
   - Background job to clean up stale sessions.

3. Integration points:
   - Login page: after successful SignIn, create session and set cookie.
   - Logout page: invalidate session (already implemented), clear cookie and redirect to login.
   - Blazor CircuitHandler: OnCircuitClosedAsync -> call SessionService.InvalidateByCircuitId
   - JS: window.addEventListener('beforeunload', call POST /api/sessions/invalidate)

4. Security considerations:
   - Use secure, HttpOnly cookies (session cookie) with SameSite=Lax.
   - Token as GUID (random) stored hashed in DB for extra protection (optional).
   - Protect invalidation endpoint (ensure token in cookie matches user) and require CSRF or use POST with same-site cookie.

## Steps & Files to Change
- Database: add migration script `Database/Scripts/006_CreateSessions.sql`.
- Data layer: add `Features/Infrastructure/Sessions/Session.cs`, `SessionsRepository.cs`, `ISessionsRepository.cs`.
- Service: `ISessionService`, `SessionService`.
- Middleware: `SessionValidationMiddleware` added to pipeline in `Program.cs` before auth.
- Login: modify `Login.razor` to call SessionService to create session/cookie.
- Logout: update `Logout.razor` to invalidate session and clear cookie (already clears via SignOut; we'll add cookie removal).
- Blazor: add `CircuitHandler` implementation `SessionCircuitHandler` to mark sessions invalid on circuit close.
- JS: add small script `_content/ValyanERP.Web/js/session-unload.js` and include it in `App.razor`.
- Background job: simple hosted service `SessionCleanupService`.
- Tests: unit tests for SessionService, integration tests for middleware and end-to-end scenario.

## Open Questions
- Cookie name to use? Suggest `ValyanERP.Session`.
- How long to keep sessions before cleanup? Suggest 1 day for expired sessions.


-- End of analysis
