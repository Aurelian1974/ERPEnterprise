CREATE OR ALTER PROCEDURE core.usp_Employee_Save
    @Id         int = NULL,
    @CompanyId  int,
    @BranchId   int = NULL,
    @EmployeeNo nvarchar(20),
    @FirstName  nvarchar(100),
    @LastName   nvarchar(100),
    @Department nvarchar(100) = NULL,
    @JobTitle   nvarchar(100) = NULL,
    @WorkEmail  nvarchar(256) = NULL,
    @Phone      nvarchar(30) = NULL,
    @HireDate   date = NULL,
    @LeaveDate  date = NULL,
    @IsActive   bit,
    @RowVersion binary(8) = NULL,
    @UserId     int
AS
BEGIN
    -- Fisa minima de angajat (02.01, 3.4), in tranzactia comenzii. Marca e unica pe firma, fara diferenta de majuscule (50018);
    -- punctul de lucru e al firmei (50021). Contul de utilizator legat preia numele si emailul din fisa (nu se editeaza pe
    -- utilizator). Firma unui angajat cu cont nu se schimba (fisa noua pentru alta firma). Data incetarii dezactiveaza contul
    -- prin jobul zilnic; autentificarea il refuza deja de la acea data (core.v_UserLoginStatus).
    -- Intoarce si contul legat, ca aplicatia sa sincronizeze emailul in contul de autentificare.
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF NOT EXISTS (SELECT 1 FROM core.Company WHERE Id = @CompanyId)
        THROW 50021, N'Firma nu există.', 1;
    IF @BranchId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM core.Branch WHERE Id = @BranchId AND CompanyId = @CompanyId)
        THROW 50021, N'Punctul de lucru nu aparține firmei.', 1;
    IF @HireDate IS NOT NULL AND @LeaveDate IS NOT NULL AND @LeaveDate < @HireDate
        THROW 50021, N'Data încetării nu poate fi înaintea datei angajării.', 1;
    IF EXISTS (SELECT 1 FROM core.Employee WHERE CompanyId = @CompanyId AND EmployeeNoKey = UPPER(@EmployeeNo) AND (@Id IS NULL OR Id <> @Id))
        THROW 50018, N'Marca este deja folosită la această firmă.', 1;

    IF @Id IS NULL
    BEGIN
        INSERT INTO core.Employee (CompanyId, BranchId, EmployeeNo, FirstName, LastName, Department, JobTitle, WorkEmail, Phone, HireDate,
            LeaveDate, IsActive, CreatedBy)
        VALUES (@CompanyId, @BranchId, @EmployeeNo, @FirstName, @LastName, @Department, @JobTitle, @WorkEmail, @Phone, @HireDate,
            @LeaveDate, @IsActive, @UserId);
        SET @Id = SCOPE_IDENTITY();
    END
    ELSE
    BEGIN
        IF EXISTS (SELECT 1 FROM core.Employee AS e JOIN core.[User] AS u ON u.EmployeeId = e.Id
                   WHERE e.Id = @Id AND e.CompanyId <> @CompanyId)
            THROW 50021, N'Angajatul are cont de utilizator; firma lui nu se schimbă.', 1;
        IF @IsActive = 0 AND EXISTS (SELECT 1 FROM core.[User] WHERE EmployeeId = @Id AND IsActive = 1)
            THROW 50010, N'Angajatul are un cont de utilizator activ; dezactivați întâi contul sau completați data încetării.', 1;

        UPDATE core.Employee
        SET CompanyId = @CompanyId, BranchId = @BranchId, EmployeeNo = @EmployeeNo, FirstName = @FirstName, LastName = @LastName,
            Department = @Department, JobTitle = @JobTitle, WorkEmail = @WorkEmail, Phone = @Phone, HireDate = @HireDate,
            LeaveDate = @LeaveDate, IsActive = @IsActive, ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE Id = @Id AND RowVersion = @RowVersion;

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM core.Employee WHERE Id = @Id)
                THROW 50027, N'Angajatul nu există.', 1;
            THROW 50006, N'Fișa angajatului a fost modificată de alt utilizator; reîncărcați datele.', 1;
        END;

        -- Contul legat urmeaza fisa (3.4: numele si emailul vin din fisa angajatului).
        UPDATE core.[User]
        SET FirstName = @FirstName, LastName = @LastName, DisplayName = @FirstName + N' ' + @LastName, Email = @WorkEmail,
            ModifiedAt = SYSUTCDATETIME(), ModifiedBy = @UserId
        WHERE EmployeeId = @Id
          AND (ISNULL(FirstName, N'') <> @FirstName OR ISNULL(LastName, N'') <> @LastName OR ISNULL(Email, N'') <> ISNULL(@WorkEmail, N''));
    END;

    SELECT e.Id, e.RowVersion, u.Id AS LinkedUserId, u.SubjectId AS LinkedSubjectId, u.UserName AS LinkedUserName,
           u.Email AS LinkedEmail, u.IsActive AS LinkedIsActive
    FROM core.Employee AS e
    LEFT JOIN core.[User] AS u ON u.EmployeeId = e.Id
    WHERE e.Id = @Id;
END;
