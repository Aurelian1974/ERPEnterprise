using System.Data.Common;

namespace Erp.Core.Contracts;

/// <summary>Contextul cererii curente: utilizator, firma, corelare, permisiuni (02.01, 6).</summary>
public interface ICurrentContext
{
    bool IsAuthenticated { get; }

    int UserId { get; }

    /// <summary>Firma curenta; 0 = nicio firma aleasa (de exemplu inainte de crearea firmelor).</summary>
    int CompanyId { get; }

    Guid CorrelationId { get; }

    /// <summary>Antetul Idempotency-Key al cererii, daca exista (4.1).</summary>
    string? IdempotencyKey { get; }

    string? IpAddress { get; }

    bool HasPermission(string permission, DimensionRef? scope = null);
}

/// <summary>O valoare de dimensiune (centru de cost, proiect, gestiune), pentru scope-ul permisiunilor.</summary>
public sealed record DimensionRef(string DimensionType, int DimensionValueId);

/// <summary>Conexiunea si tranzactia curenta, furnizate de TransactionBehavior (NET-07).</summary>
public interface IDbSession
{
    DbConnection Connection { get; }

    DbTransaction? Transaction { get; }
}
