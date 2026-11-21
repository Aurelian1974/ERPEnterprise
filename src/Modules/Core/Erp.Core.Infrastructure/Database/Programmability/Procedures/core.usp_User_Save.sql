CREATE OR ALTER PROCEDURE core.usp_User_Save
    @Id                 int = NULL,
    @SubjectId          uniqueidentifier = NULL,
    @UserName           nvarchar(50),
    @IsEmployee         bit,
    @EmployeeId         int = NULL,
    @FirstName          nvarchar(100) = NULL,
    @LastName           nvarchar(100) = NULL,
    @Email              nvarchar(256) = NULL,
    @Phone              nvarchar(30) = NULL,
    @ExternalAccessType varchar(20) = NULL,
    @ExternalPartnerId  int = NULL,
    @SponsorUserId      int = NULL,
    @AccessReason       nvarchar(500) = NULL,
    @ValidFrom          date = NULL,
    @ValidUntil         date = NULL,
    @Language           varchar(10),
    @LastCompanyId      int = NULL,
    @IsActive           bit,
    @MaxActiveUsers     int = NULL,
    @RowVersion         binary(8) = NULL,
    @UserId             int
AS
BEGIN
    -- Contul unui utilizator (02.01, 3.4; machetele 06b, 06c), in tranzactia comenzii. Trei tipuri:
    --   angajat: legat de o fisa activa de angajat; numele si emailul se copiaza din fisa (nu se editeaza pe utilizator);
    --   extern: tip acces, organizatie (partener), responsabil intern, motiv si perioada de valabilitate obligatorii (50013);
    --   de sistem (administratorul integrat): se creeaza doar de installer; tipul si numele de utilizator nu se schimba (50033).
    -- Contul tehnic „system” (Id = 1) nu se modifica. Un utilizator nou sau reactivat consuma o licenta (@MaxActiveUsers,
    -- NULL = fara limita; conturile de sistem nu se numara). Ultimul administrator activ nu se dezactiveaza (50015).
    -- Intoarce si datele necesare contului de autentificare (ids), sincronizat de aplicatie in aceeasi tranzactie.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @IsSystem bit = 0, @WasActive bit = 0, @OldUserName nvarchar(50), @DisplayName nvarchar(200);

    IF @Id = 1
        THROW 50033, N'Contul tehnic „system” nu se modifică.', 1;

    IF @Id IS NOT NULL
    BEGIN
        SELECT @IsSystem = IsSystem, @WasActive = IsActive, @OldUserName = UserName
        FROM core.[User] WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @Id;

        IF @@ROWCOUNT = 0
            THROW 50027, N'Utilizatorul nu există.', 1;
    END
    ELSE IF @SubjectId IS NULL
        THROW 50021, N'Lipsește identificatorul contului de autentificare.', 1;

    IF EXISTS (SELECT 1 FROM core.[User] WHERE UserName = @UserName AND (@Id IS NULL OR Id <> @Id))
        THROW 50031, N'Numele de utilizator este deja folosit.', 1;

    IF @IsSystem = 1
    BEGIN
        IF @IsEmployee = 1 OR @EmployeeId IS NOT NULL OR @ExternalPartnerId IS NOT NULL OR @SponsorUserId IS NOT NULL
            OR @AccessReason IS NOT NULL OR @ValidUntil IS NOT NULL OR @ExternalAccessType IS NOT NULL
            THROW 50033, N'Tipul unui cont de sistem nu se schimbă.', 1;
        IF @UserName <> @OldUserName
            THROW 50033, N'Numele de utilizator al administratorului integrat nu se schimbă (accesul de urgență îl folosește).', 1;
        SELECT @DisplayName = CASE WHEN @FirstName IS NOT NULL AND @LastName IS NOT NULL THEN @FirstName + N' ' + @LastName
                                   ELSE DisplayName END
        FROM core.[User] WHERE Id = @Id;
        SELECT @ValidFrom = NULL;
    END
    ELSE IF @IsEmployee = 1
    BEGIN
        -- Angajat: datele personale vin din fisa angajatului.
        SELECT @FirstName = FirstName, @LastName = LastName, @Email = WorkEmail, @DisplayName = FirstName + N' ' + LastName
        FROM core.Employee
        WHERE Id = @EmployeeId AND IsActive = 1;

        IF @@ROWCOUNT = 0
            THROW 50021, N'Alegeți o fișă de angajat activă.', 1;
        IF EXISTS (SELECT 1 FROM core.[User] WHERE EmployeeId = @EmployeeId AND (@Id IS NULL OR Id <> @Id))
            THROW 50012, N'Angajatul are deja un cont de utilizator.', 1;

        SELECT @Phone = NULL, @ExternalAccessType = NULL, @ExternalPartnerId = NULL, @SponsorUserId = NULL, @AccessReason = NULL,
               @ValidFrom = NULL, @ValidUntil = NULL;
    END
    ELSE
    BEGIN
        -- Extern: acces limitat in timp, cu responsabil intern si motiv.
        IF @ExternalAccessType IS NULL OR @ExternalPartnerId IS NULL OR @SponsorUserId IS NULL OR @AccessReason IS NULL
            OR @ValidFrom IS NULL OR @ValidUntil IS NULL OR @FirstName IS NULL OR @LastName IS NULL
            THROW 50013, N'Utilizatorul extern are nevoie de tip acces, organizație, responsabil, motiv, nume și perioada de acces.', 1;
        IF @ValidUntil < @ValidFrom
            THROW 50013, N'Accesul nu poate expira înainte să înceapă.', 1;
        IF NOT EXISTS (SELECT 1 FROM core.Partner WHERE Id = @ExternalPartnerId)
            THROW 50021, N'Organizația (partenerul) nu există.', 1;
        IF NOT EXISTS (
            SELECT 1 FROM core.[User]
            WHERE Id = @SponsorUserId AND Id <> 1 AND IsActive = 1 AND (IsEmployee = 1 OR IsSystem = 1)
              AND (@Id IS NULL OR Id <> @Id))
            THROW 50021, N'Responsabilul intern trebuie să fie un utilizator activ al firmei (angajat).', 1;

        SELECT @EmployeeId = NULL, @DisplayName = @FirstName + N' ' + @LastName;
    END;

    IF @LastCompanyId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @LastCompanyId)
        THROW 50021, N'Firma implicită nu există.', 1;

    -- Licenta: utilizatorii activi, fara conturile de sistem (08, 4.1, limita „users”). Blocarea pe interval evita depasirea
    -- prin doua salvari simultane.
    IF @IsActive = 1 AND @IsSystem = 0 AND @WasActive = 0 AND @MaxActiveUsers IS NOT NULL
        AND (SELECT COUNT(*) FROM core.[User] WITH (UPDLOCK, HOLDLOCK) WHERE IsActive = 1 AND IsSystem = 0) >= @MaxActiveUsers
        THROW 50032, N'S-a atins numărul de utilizatori activi permis de licență.', 1;

    -- Ultimul administrator activ la nivelul instalarii nu se dezactiveaza (A22).
    IF @Id IS NOT NULL AND @IsActive = 0 AND @WasActive = 1
        AND EXISTS (
            SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
            WHERE a.UserId = @Id AND a.CompanyId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1)
        AND NOT EXISTS (
            SELECT 1
            FROM core.UserRoleAssignment AS a
            JOIN core.Role AS r ON r.Id = a.RoleId
            JOIN core.v_UserLoginStatus AS s ON s.UserId = a.UserId
            WHERE a.UserId <> @Id AND a.CompanyId IS NULL AND r.Code = N'ADMIN' AND r.IsSystem = 1 AND s.CanSignIn = 1)
        THROW 50015, N'Este ultimul administrator activ al instalării; activați întâi alt administrator.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.[User]
            (SubjectId, UserName, DisplayName, FirstName, LastName, Email, Phone, IsActive, IsSystem, IsEmployee, EmployeeId,
             ExternalAccessType, ExternalPartnerId, SponsorUserId, AccessReason, ValidFrom, ValidUntil, Language, LastCompanyId, CreatedBy)
        VALUES
            (@SubjectId, @UserName, @DisplayName, @FirstName, @LastName, @Email, @Phone, @IsActive, 0, @IsEmployee, @EmployeeId,
             @ExternalAccessType, @ExternalPartnerId, @SponsorUserId, @AccessReason, @ValidFrom, @ValidUntil, @Language, @LastCompanyId,
             @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        UPDATE core.[User]
        SET UserName = @UserName, DisplayName = @DisplayName, FirstName = @FirstName, LastName = @LastName, Email = @Email,
            Phone = @Phone, IsActive = @IsActive, IsEmployee = @IsEmployee, EmployeeId = @EmployeeId,
            ExternalAccessType = @ExternalAccessType, ExternalPartnerId = @ExternalPartnerId, SponsorUserId = @SponsorUserId,
            AccessReason = @AccessReason, ValidFrom = @ValidFrom, ValidUntil = @ValidUntil, Language = @Language,
            LastCompanyId = @LastCompanyId, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
            THROW 50006, N'Utilizatorul a fost modificat de alt utilizator; reîncărcați datele.', 1;
    END;

    SELECT u.Id, u.RowVersion, u.SubjectId, u.UserName, u.DisplayName, u.Email, u.IsActive, @WasActive AS WasActive, u.IsSystem,
           u.IsEmployee, u.ExternalAccessType,
           CAST(CASE WHEN EXISTS (
                SELECT 1 FROM core.UserRoleAssignment AS a JOIN core.Role AS r ON r.Id = a.RoleId
                WHERE a.UserId = u.Id AND r.Code = N'ADMIN' AND r.IsSystem = 1) THEN 1 ELSE 0 END AS bit) AS IsAdmin
    FROM core.[User] AS u
    WHERE u.Id = @Id;
END;
