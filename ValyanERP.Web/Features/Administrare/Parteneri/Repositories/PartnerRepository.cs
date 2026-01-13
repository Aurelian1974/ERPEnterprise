// ========================================================================
// PartnerRepository.cs - Implementare repository Partners cu Dapper
// Vertical Slices Architecture - Features/Administrare/Parteneri
// Includes organizational security filtering via IUserPerimeterProvider
// ========================================================================

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;
using ValyanERP.Web.Features.Infrastructure.Security.Data;
using ValyanERP.Web.Features.Infrastructure.Security.Services;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe entitatea Partner.
/// Folosește stored procedures pentru toate operațiile de acces la date.
/// Implements organizational security filtering via IUserPerimeterProvider.
/// </summary>
public class PartnerRepository : IPartnerRepository
{
    private readonly DapperContext _context;
    private readonly ISecureConnectionFactory _secureConnectionFactory;
    private readonly IUserPerimeterProvider _perimeterProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<PartnerRepository> _logger;

    public PartnerRepository(
        DapperContext context, 
        ISecureConnectionFactory secureConnectionFactory,
        IUserPerimeterProvider perimeterProvider,
        ICurrentUserService currentUserService,
        ILogger<PartnerRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _secureConnectionFactory = secureConnectionFactory ?? throw new ArgumentNullException(nameof(secureConnectionFactory));
        _perimeterProvider = perimeterProvider ?? throw new ArgumentNullException(nameof(perimeterProvider));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Partner CRUD Operations

    /// <inheritdoc />
    public async Task<(IEnumerable<PartnerListDto> Partners, int TotalCount)> GetAllAsync(int skip = 0, int take = 50)
    {
        try
        {
            // Get user's perimeter for security filtering
            var perimeter = await _perimeterProvider.GetPerimeterAsync();
            var visibleCompanyIds = await _perimeterProvider.GetVisibleCompanyIdsAsync();
            var visibleWorkPlaceIds = await _perimeterProvider.GetVisibleWorkPlaceIdsAsync();
            var visibleLocationIds = await _perimeterProvider.GetVisibleLocationIdsAsync();
            
            using var connection = _context.CreateConnection();
            
            // Create TVP for company IDs
            var companyTable = new DataTable();
            companyTable.Columns.Add("Id", typeof(Guid));
            foreach (var companyId in visibleCompanyIds)
            {
                companyTable.Rows.Add(companyId);
            }
            
            // Create TVP for workplace IDs
            var workPlaceTable = new DataTable();
            workPlaceTable.Columns.Add("Id", typeof(Guid));
            foreach (var workPlaceId in visibleWorkPlaceIds)
            {
                workPlaceTable.Rows.Add(workPlaceId);
            }
            
            // Create TVP for location IDs
            var locationTable = new DataTable();
            locationTable.Columns.Add("Id", typeof(Guid));
            foreach (var locationId in visibleLocationIds)
            {
                locationTable.Rows.Add(locationId);
            }
            
            var parameters = new DynamicParameters();
            parameters.Add("@Skip", skip);
            parameters.Add("@Take", take);
            parameters.Add("@IncludeInactive", false);
            parameters.Add("@Categoria", null);
            parameters.Add("@RolPartener", null);
            parameters.Add("@TipEntitate", null);
            parameters.Add("@HasFullAccess", perimeter.HasFullAccess);
            parameters.Add("@VisibleCompanyIds", companyTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleLocationIds", locationTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleWorkPlaceIds", workPlaceTable.AsTableValuedParameter("GuidListType"));
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_Partners_GetAll_Filtered",
                parameters,
                commandType: CommandType.StoredProcedure);

            var partners = await multi.ReadAsync<PartnerListDto>();
            var totalCount = await multi.ReadSingleAsync<int>();

            return (partners, totalCount);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea listei de parteneri. Skip={Skip}, Take={Take}", skip, take);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Partner?> GetByIdAsync(Guid id)
    {
        try
        {
            // Get user's perimeter for security filtering
            var perimeter = await _perimeterProvider.GetPerimeterAsync();
            var visibleCompanyIds = await _perimeterProvider.GetVisibleCompanyIdsAsync();
            var visibleWorkPlaceIds = await _perimeterProvider.GetVisibleWorkPlaceIdsAsync();
            var visibleLocationIds = await _perimeterProvider.GetVisibleLocationIdsAsync();
            
            using var connection = _context.CreateConnection();
            
            // Create TVP for company IDs
            var companyTable = new DataTable();
            companyTable.Columns.Add("Id", typeof(Guid));
            foreach (var companyId in visibleCompanyIds)
            {
                companyTable.Rows.Add(companyId);
            }
            
            // Create TVP for workplace IDs
            var workPlaceTable = new DataTable();
            workPlaceTable.Columns.Add("Id", typeof(Guid));
            foreach (var workPlaceId in visibleWorkPlaceIds)
            {
                workPlaceTable.Rows.Add(workPlaceId);
            }
            
            // Create TVP for location IDs
            var locationTable = new DataTable();
            locationTable.Columns.Add("Id", typeof(Guid));
            foreach (var locationId in visibleLocationIds)
            {
                locationTable.Rows.Add(locationId);
            }
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", id);
            parameters.Add("@HasFullAccess", perimeter.HasFullAccess);
            parameters.Add("@VisibleCompanyIds", companyTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleLocationIds", locationTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleWorkPlaceIds", workPlaceTable.AsTableValuedParameter("GuidListType"));
            
            // Obține partenerul principal cu verificare perimetru
            var partner = await connection.QueryFirstOrDefaultAsync<Partner>(
                "sp_Partners_GetById_Filtered",
                parameters,
                commandType: CommandType.StoredProcedure);

            if (partner == null)
                return null;

            // Încarcă relațiile
            await LoadPartnerRelationsAsync(connection, partner);

            return partner;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea partenerului cu ID={Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Partner?> GetByCuiAsync(string cui)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var partner = await connection.QueryFirstOrDefaultAsync<Partner>(
                "sp_Partners_GetByCUI",
                new { CUI = cui },
                commandType: CommandType.StoredProcedure);

            if (partner != null)
            {
                await LoadPartnerRelationsAsync(connection, partner);
            }

            return partner;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea partenerului cu CUI={CUI}", cui);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Partner?> GetByCnpAsync(string cnp)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var partner = await connection.QueryFirstOrDefaultAsync<Partner>(
                "sp_Partners_GetByCNP",
                new { CNP = cnp },
                commandType: CommandType.StoredProcedure);

            if (partner != null)
            {
                await LoadPartnerRelationsAsync(connection, partner);
            }

            return partner;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea partenerului cu CNP (masked)");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<PartnerListDto> Partners, int TotalCount)> SearchAsync(
        string searchTerm, int skip = 0, int take = 50)
    {
        try
        {
            // Get user's perimeter for security filtering
            var perimeter = await _perimeterProvider.GetPerimeterAsync();
            var visibleCompanyIds = await _perimeterProvider.GetVisibleCompanyIdsAsync();
            var visibleWorkPlaceIds = await _perimeterProvider.GetVisibleWorkPlaceIdsAsync();
            var visibleLocationIds = await _perimeterProvider.GetVisibleLocationIdsAsync();
            
            using var connection = _context.CreateConnection();
            
            // Create TVP for company IDs
            var companyTable = new DataTable();
            companyTable.Columns.Add("Id", typeof(Guid));
            foreach (var companyId in visibleCompanyIds)
            {
                companyTable.Rows.Add(companyId);
            }
            
            // Create TVP for workplace IDs
            var workPlaceTable = new DataTable();
            workPlaceTable.Columns.Add("Id", typeof(Guid));
            foreach (var workPlaceId in visibleWorkPlaceIds)
            {
                workPlaceTable.Rows.Add(workPlaceId);
            }
            
            // Create TVP for location IDs
            var locationTable = new DataTable();
            locationTable.Columns.Add("Id", typeof(Guid));
            foreach (var locationId in visibleLocationIds)
            {
                locationTable.Rows.Add(locationId);
            }
            
            var parameters = new DynamicParameters();
            parameters.Add("@SearchTerm", searchTerm);
            parameters.Add("@Skip", skip);
            parameters.Add("@Take", take);
            parameters.Add("@HasFullAccess", perimeter.HasFullAccess);
            parameters.Add("@VisibleCompanyIds", companyTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleLocationIds", locationTable.AsTableValuedParameter("GuidListType"));
            parameters.Add("@VisibleWorkPlaceIds", workPlaceTable.AsTableValuedParameter("GuidListType"));
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_Partners_Search_Filtered",
                parameters,
                commandType: CommandType.StoredProcedure);

            var partners = await multi.ReadAsync<PartnerListDto>();
            var totalCount = await multi.ReadSingleAsync<int>();

            return (partners, totalCount);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la căutarea partenerilor. SearchTerm={SearchTerm}", searchTerm);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAsync(CreatePartnerDto dto, Guid createdBy)
    {
        try
        {
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Verify user has write access to target company
            // ═══════════════════════════════════════════════════════════════
            if (dto.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(dto.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to create Partner in company {CompanyId}",
                        _currentUserService.UserId, dto.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru compania selectată.");
                }
            }
            
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Categoria", (int)dto.Categoria);
            parameters.Add("@TipEntitate", dto.TipEntitate);
            parameters.Add("@RolPartener", (int)dto.RolPartener);
            parameters.Add("@Denumire", dto.Denumire);
            parameters.Add("@DenumireScurta", dto.DenumireScurta);
            parameters.Add("@Nume", dto.Nume);
            parameters.Add("@Prenume", dto.Prenume);
            parameters.Add("@CNP", dto.CNP);
            parameters.Add("@CUI", dto.CUI);
            parameters.Add("@CIF", dto.CIF);
            parameters.Add("@VATID", dto.VATID);
            parameters.Add("@RegCom", dto.RegCom);
            parameters.Add("@NrAutorizatie", dto.NrAutorizatie);
            parameters.Add("@Pasaport", dto.Pasaport);
            parameters.Add("@TaraOrigine", dto.TaraOrigine);
            parameters.Add("@CAENPrincipal", dto.CAENPrincipal);
            parameters.Add("@CapitalSocial", dto.CapitalSocial);
            parameters.Add("@Email", dto.Email);
            parameters.Add("@Telefon", dto.Telefon);
            parameters.Add("@TelefonSecundar", dto.TelefonSecundar);
            parameters.Add("@Website", dto.Website);
            parameters.Add("@EstePlatitorTVA", dto.EstePlatitorTVA);
            parameters.Add("@DataInregistrareTVA", dto.DataInregistrareTVA);
            parameters.Add("@StatusSplitTVA", dto.StatusSplitTVA);
            parameters.Add("@LimitaCredit", dto.LimitaCredit);
            parameters.Add("@TermenPlataDef", dto.TermenPlataDef);
            parameters.Add("@CategorieComercialaTxt", (string?)null);
            parameters.Add("@Observatii", dto.Observatii);
            parameters.Add("@CreatedBy", createdBy);
            
            // Ownership - în ce entitate organizațională a fost creat partenerul
            parameters.Add("@OwnerCompanyId", dto.OwnerCompanyId);
            parameters.Add("@OwnerWorkPlaceId", dto.OwnerWorkPlaceId);
            parameters.Add("@OwnerLocationId", dto.OwnerLocationId);

            // SP returnează Id și Cod
            var result = await connection.QuerySingleAsync<dynamic>(
                "sp_Partners_Create",
                parameters,
                commandType: CommandType.StoredProcedure);

            var id = (Guid)result.Id;
            var cod = (string)result.Cod;

            _logger.LogInformation("Partener creat cu succes. ID={Id}, Cod={Cod}", id, cod);
            return id;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la crearea partenerului. TipEntitate={TipEntitate}", dto.TipEntitate);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAsync(UpdatePartnerDto dto, Guid updatedBy)
    {
        try
        {
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Get existing partner and verify access
            // ═══════════════════════════════════════════════════════════════
            var existingPartner = await GetByIdAsync(dto.Id);
            if (existingPartner == null)
            {
                _logger.LogWarning("Partenerul nu a fost găsit pentru actualizare. ID={Id}", dto.Id);
                return false;
            }
            
            // Check write access to current owner company
            if (existingPartner.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(existingPartner.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to update Partner {PartnerId} owned by company {CompanyId}",
                        _currentUserService.UserId, dto.Id, existingPartner.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru acest partener.");
                }
            }
            
            // If changing ownership, verify access to new company too
            if (dto.OwnerCompanyId.HasValue && dto.OwnerCompanyId != existingPartner.OwnerCompanyId)
            {
                var canWriteNew = await _perimeterProvider.CanWriteToCompanyAsync(dto.OwnerCompanyId.Value);
                if (!canWriteNew)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to transfer Partner {PartnerId} to company {CompanyId}",
                        _currentUserService.UserId, dto.Id, dto.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de scriere pentru compania destinație.");
                }
            }
            
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", dto.Id);
            parameters.Add("@Categoria", (int)dto.Categoria);
            parameters.Add("@TipEntitate", dto.TipEntitate);
            parameters.Add("@RolPartener", (int)dto.RolPartener);
            parameters.Add("@Denumire", dto.Denumire);
            parameters.Add("@DenumireScurta", dto.DenumireScurta);
            parameters.Add("@Nume", dto.Nume);
            parameters.Add("@Prenume", dto.Prenume);
            parameters.Add("@CNP", dto.CNP);
            parameters.Add("@CUI", dto.CUI);
            parameters.Add("@CIF", dto.CIF);
            parameters.Add("@RegCom", dto.RegCom);
            parameters.Add("@TaraOrigine", dto.TaraOrigine);
            parameters.Add("@Email", dto.Email);
            parameters.Add("@Telefon", dto.Telefon);
            parameters.Add("@EstePlatitorTVA", dto.EstePlatitorTVA);
            parameters.Add("@PartnerStatus", dto.PartnerStatus);
            parameters.Add("@Observatii", dto.Observatii);
            parameters.Add("@IsActive", dto.EsteActiv); // SP uses @IsActive, not @EsteActiv
            parameters.Add("@UpdatedBy", updatedBy);
            
            // Ownership - în ce entitate organizațională aparține partenerul
            parameters.Add("@OwnerCompanyId", dto.OwnerCompanyId);
            parameters.Add("@OwnerWorkPlaceId", dto.OwnerWorkPlaceId);
            parameters.Add("@OwnerLocationId", dto.OwnerLocationId);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Partener actualizat cu succes. ID={Id}", dto.Id);
                return true;
            }

            _logger.LogWarning("Partenerul nu a fost găsit pentru actualizare. ID={Id}", dto.Id);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea partenerului. ID={Id}", dto.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, Guid deletedBy)
    {
        try
        {
            // ═══════════════════════════════════════════════════════════════
            // SECURITY CHECK: Get existing partner and verify write access
            // ═══════════════════════════════════════════════════════════════
            var existingPartner = await GetByIdAsync(id);
            if (existingPartner == null)
            {
                _logger.LogWarning("Partenerul nu a fost găsit pentru ștergere. ID={Id}", id);
                return false;
            }
            
            if (existingPartner.OwnerCompanyId.HasValue)
            {
                var canWrite = await _perimeterProvider.CanWriteToCompanyAsync(existingPartner.OwnerCompanyId.Value);
                if (!canWrite)
                {
                    _logger.LogWarning(
                        "ACCESS DENIED: User {UserId} attempted to delete Partner {PartnerId} owned by company {CompanyId}",
                        _currentUserService.UserId, id, existingPartner.OwnerCompanyId.Value);
                    throw new UnauthorizedAccessException(
                        "Nu aveți permisiune de ștergere pentru acest partener.");
                }
            }
            
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_Delete",
                new { Id = id, UpdatedBy = deletedBy },
                commandType: CommandType.StoredProcedure);

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Partener șters (soft delete) cu succes. ID={Id}", id);
                return true;
            }

            _logger.LogWarning("Partenerul nu a fost găsit pentru ștergere. ID={Id}", id);
            return false;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea partenerului. ID={Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAnafStatusAsync(Guid id, bool? estePlatitorTva, bool? statusSplitTva,
        bool? esteInactiv, bool? esteInsolvent, DateTime dataVerificareAnaf, Guid? updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_UpdateAnafStatus",
                new 
                { 
                    Id = id, 
                    EstePlatitorTVA = estePlatitorTva,
                    StatusSplitTVA = statusSplitTva,
                    EsteInactiv = esteInactiv, 
                    EsteInsolvent = esteInsolvent,
                    DataVerificareANAF = dataVerificareAnaf,
                    UpdatedBy = updatedBy
                },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea statusului ANAF. ID={Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateFromAnafAsync(Guid id, AnafVerificationCache anafData, Guid? updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_UpdateFromAnaf",
                new 
                { 
                    Id = id, 
                    Denumire = anafData.Denumire,
                    Telefon = anafData.Telefon,
                    RegCom = anafData.NrRegCom,
                    CAENPrincipal = (string?)null, // CAEN not in cache model yet
                    EstePlatitorTVA = anafData.ScpTVA,
                    DataInregistrareTVA = anafData.DataInregistrareTVA,
                    StatusSplitTVA = anafData.StatusSplitTVA,
                    EsteInactiv = anafData.StatusInactivi,
                    EsteInsolvent = anafData.StatusInsolventa,
                    StatusRoEfactura = anafData.StatusRoEfactura,
                    UpdatedBy = updatedBy
                },
                commandType: CommandType.StoredProcedure);

            if (rowsAffected > 0)
            {
                _logger.LogInformation("Partener actualizat din ANAF. ID={Id}, Denumire={Denumire}", id, anafData.Denumire);
                return true;
            }

            _logger.LogWarning("Partenerul nu a fost găsit pentru actualizare ANAF. ID={Id}", id);
            return false;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea partenerului din ANAF. ID={Id}", id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid?> UpsertSediuAddressFromAnafAsync(Guid partnerId, AnafVerificationCache anafData, Guid? updatedBy)
    {
        try
        {
            // Verifică dacă avem date pentru adresa sediu
            if (string.IsNullOrWhiteSpace(anafData.SediuStrada) && 
                string.IsNullOrWhiteSpace(anafData.SediuLocalitate))
            {
                _logger.LogDebug("Nu există date pentru adresa sediu din ANAF pentru PartnerId={PartnerId}", partnerId);
                return null;
            }

            // Construiește adresa completă
            var adresaParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(anafData.SediuStrada))
                adresaParts.Add(anafData.SediuStrada);
            if (!string.IsNullOrWhiteSpace(anafData.SediuNumar))
                adresaParts.Add($"Nr. {anafData.SediuNumar}");
            if (!string.IsNullOrWhiteSpace(anafData.SediuDetalii))
                adresaParts.Add(anafData.SediuDetalii);
            
            var adresaCompleta = string.Join(", ", adresaParts);

            using var connection = _context.CreateConnection();
            
            var result = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "sp_PartnerAddresses_UpsertFromAnaf",
                new 
                { 
                    PartnerId = partnerId,
                    Adresa = adresaCompleta,
                    Localitate = anafData.SediuLocalitate ?? string.Empty,
                    Judet = anafData.SediuJudet ?? string.Empty,
                    CodPostal = anafData.SediuCodPostal,
                    Tara = anafData.SediuTara ?? "România",
                    UpdatedBy = updatedBy
                },
                commandType: CommandType.StoredProcedure);

            if (result != null)
            {
                var addressId = (Guid)result.Id;
                var operation = (string)result.Operation;
                _logger.LogInformation(
                    "Adresa sediu {Operation} din ANAF. PartnerId={PartnerId}, AddressId={AddressId}, Adresa={Adresa}", 
                    operation, partnerId, addressId, adresaCompleta);
                return addressId;
            }

            return null;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la upsert adresă sediu din ANAF. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetPrincipalAddressAsync(Guid partnerId, Guid addressId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_SetPrincipalAddress",
                new { PartnerId = partnerId, AddressId = addressId },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la setarea adresei principale. PartnerId={PartnerId}, AddressId={AddressId}", 
                partnerId, addressId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateCodeAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Code", dbType: DbType.String, size: 20, direction: ParameterDirection.Output);

            await connection.ExecuteAsync(
                "sp_Partners_GenerateCode",
                parameters,
                commandType: CommandType.StoredProcedure);

            return parameters.Get<string>("@Code");
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la generarea codului de partener");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCuiAsync(string cui, Guid? excludeId = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[Partners] 
                    WHERE CUI = @CUI 
                    AND IsActive = 1 
                    AND (@ExcludeId IS NULL OR Id != @ExcludeId)
                ) THEN 1 ELSE 0 END";

            return await connection.QuerySingleAsync<bool>(sql, new { CUI = cui, ExcludeId = excludeId });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la verificarea existenței CUI={CUI}", cui);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsByCnpAsync(string cnp, Guid? excludeId = null)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            const string sql = @"
                SELECT CASE WHEN EXISTS (
                    SELECT 1 FROM [dbo].[Partners] 
                    WHERE CNP = @CNP 
                    AND IsActive = 1 
                    AND (@ExcludeId IS NULL OR Id != @ExcludeId)
                ) THEN 1 ELSE 0 END";

            return await connection.QuerySingleAsync<bool>(sql, new { CNP = cnp, ExcludeId = excludeId });
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la verificarea existenței CNP (masked)");
            throw;
        }
    }

    #endregion

    #region Partner Addresses

    /// <inheritdoc />
    public async Task<IEnumerable<PartnerAddress>> GetAddressesByPartnerIdAsync(Guid partnerId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryAsync<PartnerAddress>(
                "sp_PartnerAddresses_GetByPartnerId",
                new { PartnerId = partnerId },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea adreselor partenerului. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateAddressAsync(Guid partnerId, PartnerAddress address, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@PartnerId", partnerId);
            parameters.Add("@TipAdresa", (int)address.TipAdresa);
            parameters.Add("@Denumire", address.Denumire);
            parameters.Add("@Adresa", address.Adresa);
            parameters.Add("@Localitate", address.Localitate);
            parameters.Add("@Judet", address.Judet);
            parameters.Add("@CodPostal", address.CodPostal);
            parameters.Add("@Tara", address.Tara);
            parameters.Add("@CodTaraISO", address.CodTaraISO);
            parameters.Add("@Telefon", address.Telefon);
            parameters.Add("@Email", address.Email);
            parameters.Add("@PersoanaContact", address.PersoanaContact);
            parameters.Add("@EstePrincipala", address.EstePrincipala);
            parameters.Add("@CreatedBy", createdBy);

            return await connection.QuerySingleAsync<Guid>(
                "sp_PartnerAddresses_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la crearea adresei. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateAddressAsync(PartnerAddress address, Guid updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", address.Id);
            parameters.Add("@TipAdresa", (int)address.TipAdresa);
            parameters.Add("@Denumire", address.Denumire);
            parameters.Add("@Adresa", address.Adresa);
            parameters.Add("@Localitate", address.Localitate);
            parameters.Add("@Judet", address.Judet);
            parameters.Add("@CodPostal", address.CodPostal);
            parameters.Add("@Tara", address.Tara);
            parameters.Add("@CodTaraISO", address.CodTaraISO);
            parameters.Add("@Telefon", address.Telefon);
            parameters.Add("@Email", address.Email);
            parameters.Add("@PersoanaContact", address.PersoanaContact);
            parameters.Add("@EstePrincipala", address.EstePrincipala);
            parameters.Add("@UpdatedBy", updatedBy);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerAddresses_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea adresei. Id={Id}", address.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAddressAsync(Guid addressId, Guid deletedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerAddresses_Delete",
                new { Id = addressId, DeletedBy = deletedBy },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea adresei. Id={Id}", addressId);
            throw;
        }
    }

    #endregion

    #region Partner Contacts

    /// <inheritdoc />
    public async Task<IEnumerable<PartnerContact>> GetContactsByPartnerIdAsync(Guid partnerId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryAsync<PartnerContact>(
                "sp_PartnerContacts_GetByPartnerId",
                new { PartnerId = partnerId },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea contactelor partenerului. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateContactAsync(Guid partnerId, PartnerContact contact, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@PartnerId", partnerId);
            parameters.Add("@Nume", contact.Nume);
            parameters.Add("@Prenume", contact.Prenume);
            parameters.Add("@Functie", contact.Functie);
            parameters.Add("@Departament", contact.Departament);
            parameters.Add("@Email", contact.Email);
            parameters.Add("@Telefon", contact.Telefon);
            parameters.Add("@TelefonMobil", contact.TelefonMobil);
            parameters.Add("@EsteDecident", contact.EsteDecident);
            parameters.Add("@EstePrincipal", contact.EstePrincipal);
            parameters.Add("@Observatii", contact.Observatii);
            parameters.Add("@CreatedBy", createdBy);

            return await connection.QuerySingleAsync<Guid>(
                "sp_PartnerContacts_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la crearea contactului. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateContactAsync(PartnerContact contact, Guid updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", contact.Id);
            parameters.Add("@Nume", contact.Nume);
            parameters.Add("@Prenume", contact.Prenume);
            parameters.Add("@Functie", contact.Functie);
            parameters.Add("@Departament", contact.Departament);
            parameters.Add("@Email", contact.Email);
            parameters.Add("@Telefon", contact.Telefon);
            parameters.Add("@TelefonMobil", contact.TelefonMobil);
            parameters.Add("@EsteDecident", contact.EsteDecident);
            parameters.Add("@EstePrincipal", contact.EstePrincipal);
            parameters.Add("@Observatii", contact.Observatii);
            parameters.Add("@UpdatedBy", updatedBy);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerContacts_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea contactului. Id={Id}", contact.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteContactAsync(Guid contactId, Guid deletedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerContacts_Delete",
                new { Id = contactId, DeletedBy = deletedBy },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea contactului. Id={Id}", contactId);
            throw;
        }
    }

    #endregion

    #region Partner Bank Accounts

    /// <inheritdoc />
    public async Task<IEnumerable<PartnerBankAccount>> GetBankAccountsByPartnerIdAsync(Guid partnerId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryAsync<PartnerBankAccount>(
                "sp_PartnerBankAccounts_GetByPartnerId",
                new { PartnerId = partnerId },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea conturilor bancare. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateBankAccountAsync(Guid partnerId, PartnerBankAccount bankAccount, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@PartnerId", partnerId);
            parameters.Add("@IBAN", bankAccount.IBAN);
            parameters.Add("@BIC", bankAccount.BIC);
            parameters.Add("@NumeBanca", bankAccount.NumeBanca);
            parameters.Add("@Sucursala", bankAccount.Sucursala);
            parameters.Add("@Moneda", bankAccount.Moneda);
            parameters.Add("@TitularCont", bankAccount.TitularCont);
            parameters.Add("@EstePrincipal", bankAccount.EstePrincipal);
            parameters.Add("@Observatii", bankAccount.Observatii);
            parameters.Add("@CreatedBy", createdBy);

            return await connection.QuerySingleAsync<Guid>(
                "sp_PartnerBankAccounts_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la crearea contului bancar. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateBankAccountAsync(PartnerBankAccount bankAccount, Guid updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", bankAccount.Id);
            parameters.Add("@IBAN", bankAccount.IBAN);
            parameters.Add("@BIC", bankAccount.BIC);
            parameters.Add("@NumeBanca", bankAccount.NumeBanca);
            parameters.Add("@Sucursala", bankAccount.Sucursala);
            parameters.Add("@Moneda", bankAccount.Moneda);
            parameters.Add("@TitularCont", bankAccount.TitularCont);
            parameters.Add("@EstePrincipal", bankAccount.EstePrincipal);
            parameters.Add("@Observatii", bankAccount.Observatii);
            parameters.Add("@UpdatedBy", updatedBy);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerBankAccounts_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea contului bancar. Id={Id}", bankAccount.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteBankAccountAsync(Guid bankAccountId, Guid deletedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerBankAccounts_Delete",
                new { Id = bankAccountId, DeletedBy = deletedBy },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea contului bancar. Id={Id}", bankAccountId);
            throw;
        }
    }

    #endregion

    #region Partner Representatives

    /// <inheritdoc />
    public async Task<IEnumerable<PartnerRepresentative>> GetRepresentativesByPartnerIdAsync(Guid partnerId)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryAsync<PartnerRepresentative>(
                "sp_PartnerRepresentatives_GetByPartnerId",
                new { PartnerId = partnerId },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea reprezentanților. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> CreateRepresentativeAsync(Guid partnerId, PartnerRepresentative representative, Guid createdBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@PartnerId", partnerId);
            parameters.Add("@PersoanaId", representative.PersoanaId);
            parameters.Add("@Nume", representative.Nume);
            parameters.Add("@Prenume", representative.Prenume);
            parameters.Add("@CNP", representative.CNP);
            parameters.Add("@Functie", representative.Functie);
            parameters.Add("@TipReprezentant", (int)representative.TipReprezentant);
            parameters.Add("@ArePutereSemnatura", representative.ArePutereSemnatura);
            parameters.Add("@LimitaSemnatura", representative.LimitaSemnatura);
            parameters.Add("@DataNumire", representative.DataNumire);
            parameters.Add("@DataExpirare", representative.DataExpirare);
            parameters.Add("@Email", representative.Email);
            parameters.Add("@Telefon", representative.Telefon);
            parameters.Add("@Observatii", representative.Observatii);
            parameters.Add("@CreatedBy", createdBy);

            return await connection.QuerySingleAsync<Guid>(
                "sp_PartnerRepresentatives_Create",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la crearea reprezentantului. PartnerId={PartnerId}", partnerId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UpdateRepresentativeAsync(PartnerRepresentative representative, Guid updatedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@Id", representative.Id);
            parameters.Add("@PersoanaId", representative.PersoanaId);
            parameters.Add("@Nume", representative.Nume);
            parameters.Add("@Prenume", representative.Prenume);
            parameters.Add("@CNP", representative.CNP);
            parameters.Add("@Functie", representative.Functie);
            parameters.Add("@TipReprezentant", (int)representative.TipReprezentant);
            parameters.Add("@ArePutereSemnatura", representative.ArePutereSemnatura);
            parameters.Add("@LimitaSemnatura", representative.LimitaSemnatura);
            parameters.Add("@DataNumire", representative.DataNumire);
            parameters.Add("@DataExpirare", representative.DataExpirare);
            parameters.Add("@Email", representative.Email);
            parameters.Add("@Telefon", representative.Telefon);
            parameters.Add("@Observatii", representative.Observatii);
            parameters.Add("@UpdatedBy", updatedBy);

            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerRepresentatives_Update",
                parameters,
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la actualizarea reprezentantului. Id={Id}", representative.Id);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteRepresentativeAsync(Guid representativeId, Guid deletedBy)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_PartnerRepresentatives_Delete",
                new { Id = representativeId, DeletedBy = deletedBy },
                commandType: CommandType.StoredProcedure);

            return rowsAffected > 0;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la ștergerea reprezentantului. Id={Id}", representativeId);
            throw;
        }
    }

    #endregion

    #region ANAF Verification Cache

    /// <inheritdoc />
    public async Task<AnafVerificationCache?> GetAnafCacheAsync(string cui)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QueryFirstOrDefaultAsync<AnafVerificationCache>(
                "sp_AnafCache_Get",
                new { CUI = cui },
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la obținerea cache-ului ANAF. CUI={CUI}", cui);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Guid> SaveAnafCacheAsync(AnafVerificationCache cache)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var parameters = new DynamicParameters();
            parameters.Add("@CUI", cache.CUI);
            parameters.Add("@Denumire", cache.Denumire);
            parameters.Add("@Adresa", cache.Adresa);
            parameters.Add("@NrRegCom", cache.NrRegCom);
            // Procedura stocată folosește parametri specifici - aliniem cu signatura SP
            parameters.Add("@ScpTVA", cache.ScpTVA);
            parameters.Add("@DataInregistrareTVA", cache.DataInregistrareTVA);
            parameters.Add("@DataAnulareTVA", cache.DataAnulareTVA);
            parameters.Add("@StatusTVA", cache.StatusTVA);
            parameters.Add("@StatusSplitTVA", cache.StatusSplitTVA);
            parameters.Add("@DataInceputSplitTVA", cache.DataInceputSplitTVA);
            parameters.Add("@StatusInactivi", cache.StatusInactivi);
            parameters.Add("@DataInactivare", cache.DataInactivare);
            parameters.Add("@StatusRoEfactura", cache.StatusRoEfactura);
            parameters.Add("@DataInceputRoEfactura", cache.DataInceputRoEfactura);
            parameters.Add("@RawResponse", cache.RawResponse);
            parameters.Add("@DataInterogare", cache.DataInterogare == DateTime.MinValue ? DateTime.Today : cache.DataInterogare);
            parameters.Add("@CacheDurationHours", 24); // SP calculează ExpiresAt intern
            parameters.Add("@CreatedBy", cache.CreatedBy);

            return await connection.QuerySingleAsync<Guid>(
                "sp_AnafCache_Save",
                parameters,
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la salvarea cache-ului ANAF. CUI={CUI}", cache.CUI);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupExpiredAnafCacheAsync()
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            return await connection.QuerySingleAsync<int>(
                "sp_AnafCache_Cleanup",
                commandType: CommandType.StoredProcedure);
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Eroare la curățarea cache-ului ANAF expirat");
            throw;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Încarcă relațiile unui partener (adrese, contacte, conturi bancare, reprezentanți).
    /// </summary>
    private async Task LoadPartnerRelationsAsync(System.Data.IDbConnection connection, Partner partner)
    {
        // Încarcă adresele
        var addresses = await connection.QueryAsync<PartnerAddress>(
            "sp_PartnerAddresses_GetByPartnerId",
            new { PartnerId = partner.Id },
            commandType: CommandType.StoredProcedure);
        partner.Addresses = addresses.ToList();

        // Încarcă contactele
        var contacts = await connection.QueryAsync<PartnerContact>(
            "sp_PartnerContacts_GetByPartnerId",
            new { PartnerId = partner.Id },
            commandType: CommandType.StoredProcedure);
        partner.Contacts = contacts.ToList();

        // Încarcă conturile bancare
        var bankAccounts = await connection.QueryAsync<PartnerBankAccount>(
            "sp_PartnerBankAccounts_GetByPartnerId",
            new { PartnerId = partner.Id },
            commandType: CommandType.StoredProcedure);
        partner.BankAccounts = bankAccounts.ToList();

        // Încarcă reprezentanții
        var representatives = await connection.QueryAsync<PartnerRepresentative>(
            "sp_PartnerRepresentatives_GetByPartnerId",
            new { PartnerId = partner.Id },
            commandType: CommandType.StoredProcedure);
        partner.Representatives = representatives.ToList();
    }

    #endregion
}
