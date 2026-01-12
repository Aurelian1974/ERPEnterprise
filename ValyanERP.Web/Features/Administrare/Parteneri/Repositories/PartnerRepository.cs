// ========================================================================
// PartnerRepository.cs - Implementare repository Partners cu Dapper
// Vertical Slices Architecture - Features/Administrare/Parteneri
// ========================================================================

using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using ValyanERP.Web.Features.Administrare.Parteneri.Models;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.DTOs;
using ValyanERP.Web.Features.Administrare.Parteneri.Models.Enums;
using ValyanERP.Web.Infrastructure.Data;

namespace ValyanERP.Web.Features.Administrare.Parteneri.Repositories;

/// <summary>
/// Repository pentru operații CRUD pe entitatea Partner.
/// Folosește stored procedures pentru toate operațiile de acces la date.
/// </summary>
public class PartnerRepository : IPartnerRepository
{
    private readonly DapperContext _context;
    private readonly ILogger<PartnerRepository> _logger;

    public PartnerRepository(DapperContext context, ILogger<PartnerRepository> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Partner CRUD Operations

    /// <inheritdoc />
    public async Task<(IEnumerable<PartnerListDto> Partners, int TotalCount)> GetAllAsync(int skip = 0, int take = 50)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_Partners_GetAll",
                new { Skip = skip, Take = take },
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
            using var connection = _context.CreateConnection();
            
            // Obține partenerul principal
            var partner = await connection.QueryFirstOrDefaultAsync<Partner>(
                "sp_Partners_GetById",
                new { Id = id },
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
            using var connection = _context.CreateConnection();
            
            using var multi = await connection.QueryMultipleAsync(
                "sp_Partners_Search",
                new { SearchTerm = searchTerm, Skip = skip, Take = take },
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
            parameters.Add("@EstePlătitorTVA", dto.EstePlatitorTVA);
            parameters.Add("@DataInregistrareTVA", dto.DataInregistrareTVA);
            parameters.Add("@StatusSplitTVA", dto.StatusSplitTVA);
            parameters.Add("@LimitaCredit", dto.LimitaCredit);
            parameters.Add("@TermenPlataDef", dto.TermenPlataDef);
            parameters.Add("@Observatii", dto.Observatii);
            parameters.Add("@CreatedBy", createdBy);

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
            parameters.Add("@EstePlătitorTVA", dto.EstePlatitorTVA);
            parameters.Add("@DataInregistrareTVA", dto.DataInregistrareTVA);
            parameters.Add("@StatusSplitTVA", dto.StatusSplitTVA);
            parameters.Add("@PartnerStatus", dto.PartnerStatus);
            parameters.Add("@EsteActiv", dto.EsteActiv);
            parameters.Add("@BlocatFacturare", dto.BlocatFacturare);
            parameters.Add("@BlocatLivrare", dto.BlocatLivrare);
            parameters.Add("@MotivBlocare", dto.MotivBlocare);
            parameters.Add("@LimitaCredit", dto.LimitaCredit);
            parameters.Add("@TermenPlataDef", dto.TermenPlataDef);
            parameters.Add("@Observatii", dto.Observatii);
            parameters.Add("@UpdatedBy", updatedBy);

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
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_Delete",
                new { Id = id, DeletedBy = deletedBy },
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
    public async Task<bool> UpdateAnafStatusAsync(Guid id, bool? esteInactiv, bool? esteRadiat, 
        bool? estePlatitorTva, bool? esteInsolvent, DateTime dataVerificareAnaf)
    {
        try
        {
            using var connection = _context.CreateConnection();
            
            var rowsAffected = await connection.ExecuteAsync(
                "sp_Partners_UpdateAnafStatus",
                new 
                { 
                    Id = id, 
                    EsteInactiv = esteInactiv, 
                    EsteRadiat = esteRadiat,
                    EstePlatitorTVA = estePlatitorTva,
                    EsteInsolvent = esteInsolvent,
                    DataVerificareANAF = dataVerificareAnaf
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
            parameters.Add("@CodPostal", cache.CodPostal);
            parameters.Add("@Telefon", cache.Telefon);
            parameters.Add("@Fax", cache.Fax);
            parameters.Add("@ScpTVA", cache.ScpTVA);
            parameters.Add("@DataInregistrareTVA", cache.DataInregistrareTVA);
            parameters.Add("@DataAnulareTVA", cache.DataAnulareTVA);
            parameters.Add("@StatusTVA", cache.StatusTVA);
            parameters.Add("@StatusTVAMessage", cache.StatusTVAMessage);
            parameters.Add("@StatusSplitTVA", cache.StatusSplitTVA);
            parameters.Add("@DataInceputSplitTVA", cache.DataInceputSplitTVA);
            parameters.Add("@DataAnulareSplitTVA", cache.DataAnulareSplitTVA);
            parameters.Add("@StatusInactivi", cache.StatusInactivi);
            parameters.Add("@DataInactivare", cache.DataInactivare);
            parameters.Add("@DataReactivare", cache.DataReactivare);
            parameters.Add("@StatusInactiviMessage", cache.StatusInactiviMessage);
            parameters.Add("@StatusRoEFactura", cache.StatusRoEFactura);
            parameters.Add("@DataInceputRoEFactura", cache.DataInceputRoEFactura);
            parameters.Add("@RawResponse", cache.RawResponse);
            parameters.Add("@DataInterogare", cache.DataInterogare);
            parameters.Add("@VerifiedAt", cache.VerifiedAt);
            parameters.Add("@ExpiresAt", cache.ExpiresAt);
            parameters.Add("@Source", cache.Source);
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
