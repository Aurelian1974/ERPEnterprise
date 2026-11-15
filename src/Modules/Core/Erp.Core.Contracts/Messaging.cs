using MediatR;

namespace Erp.Core.Contracts;

/// <summary>Comanda: modifica date; trece prin tot pipeline-ul (02.01, 4.1), inclusiv tranzactia si audit-ul.</summary>
public interface ICommand<out TResult> : IRequest<TResult>
{
}

/// <summary>Interogarea: doar citire; trece prin corelare, licenta si autorizare, fara tranzactie de scriere.</summary>
public interface IQuery<out TResult> : IRequest<TResult>
{
}

public interface ICommandHandler<TCommand, TResult> : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
}

public interface IQueryHandler<TQuery, TResult> : IRequestHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
}

/// <summary>Comenzile cu data contabila: PeriodLockBehavior refuza perioadele inchise (P4, 4.1 pasul 5).</summary>
public interface IAccountingDated
{
    DateOnly AccountingDate { get; }
}

/// <summary>Modulul comenzii, verificat in licenta (4.1 pasul 2). Codurile sunt cele din licenta (de exemplu „Core”, „Sales”).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ModuleAttribute(string moduleCode) : Attribute
{
    public string ModuleCode { get; } = moduleCode;
}

/// <summary>Permisiunea ceruta (4.1 pasul 3, NET-04). Format Modul.Resursa.Actiune, comparat exact (NET-21).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Interogari permise oricarui utilizator autentificat, fara permisiune anume (de exemplu profilul propriu).
/// Comenzile nu pot folosi atributul: orice modificare cere o permisiune declarata (NET-04).
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuthenticatedUserAttribute : Attribute
{
}

/// <summary>Comanda scrie in jurnalul de audit, in aceeasi tranzactie (4.1 pasul 8, P5).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute(string action) : Attribute
{
    public string Action { get; } = action;
}
