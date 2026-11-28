using Erp.Core.Contracts;
using Erp.Inventory.Contracts;
using FluentValidation;

namespace Erp.Inventory.Application.Warehouses;

/// <summary>Un gestionar al gestiunii, cu perioada lui (S1 3.2; macheta s1-02). LeaveDate = plecarea angajatului din firma.</summary>
public sealed record WarehouseKeeperDto(int Id, int EmployeeId, string EmployeeName, byte Role, DateOnly ValidFrom, DateOnly? ValidTo,
    string? GuaranteeNote, int? HandoverId, DateOnly? LeaveDate, byte[] RowVersion);

/// <summary>O predare-primire a gestiunii (S1 3.2). Status: 1 Ciorna, 2 Inventar in curs, 3 Finalizata, 4 Anulata.</summary>
public sealed record WarehouseHandoverDto(int Id, DateOnly HandoverDate, int? FromEmployeeId, string? FromEmployeeName, int ToEmployeeId,
    string ToEmployeeName, bool RequiresCount, int? InventoryCountId, byte Status, string? Notes, DateTime? CompletedAt, byte[] RowVersion);

/// <summary>Un angajat activ al firmei, pentru alegerea gestionarului.</summary>
public sealed record KeeperEmployeeDto(int Id, string Name, DateOnly? LeaveDate);

/// <summary>Tab-ul Gestionari: gestionarii, predarile si angajatii care pot fi alesi.</summary>
public sealed record WarehouseKeepersDto(IReadOnlyList<WarehouseKeeperDto> Keepers, IReadOnlyList<WarehouseHandoverDto> Handovers,
    IReadOnlyList<KeeperEmployeeDto> Employees);

/// <summary>Rezultatul finalizarii unei predari: datele evenimentului GestionarSchimbat.</summary>
public sealed record HandoverCompleted(int Id, byte[] RowVersion, int WarehouseId, DateOnly HandoverDate, int? FromEmployeeId, int ToEmployeeId,
    int? InventoryCountId);

public sealed record WarehouseKeeperRecord(int? Id, int WarehouseId, int EmployeeId, byte Role, DateOnly ValidFrom, DateOnly? ValidTo,
    string? GuaranteeNote, byte[]? RowVersion);

/// <summary>Accesul la date pentru gestionari si predari (inv.usp_WarehouseKeeper_*, inv.usp_WarehouseHandover_*).</summary>
public interface IWarehouseKeeperStore
{
    Task<WarehouseKeepersDto> ListAsync(int companyId, int warehouseId, CancellationToken ct);

    Task<InventorySaved> SaveKeeperAsync(int companyId, WarehouseKeeperRecord keeper, int userId, CancellationToken ct);

    Task<InventorySaved> StartHandoverAsync(int companyId, int warehouseId, DateOnly handoverDate, int toEmployeeId, string? notes, int userId,
        CancellationToken ct);

    Task<HandoverCompleted> CompleteHandoverAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);

    Task<InventorySaved> CancelHandoverAsync(int companyId, int id, byte[] rowVersion, int userId, CancellationToken ct);
}

/// <summary>Gestionarii si predarile unei gestiuni (S1 3.2; macheta s1-02).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseView)]
public sealed record GetWarehouseKeepers(int WarehouseId) : IQuery<WarehouseKeepersDto>;

/// <summary>Adauga sau modifica un gestionar (titular sau inlocuitor) cu perioada lui; titularii nu se suprapun (54009).</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseKeeper)]
[Audited("Inv.WarehouseKeeper.Save")]
public sealed record SaveWarehouseKeeper(int? Id, int WarehouseId, int EmployeeId, byte Role, DateOnly ValidFrom, DateOnly? ValidTo,
    string? GuaranteeNote, byte[]? RowVersion) : ICommand<InventorySaved>;

/// <summary>Porneste predarea-primirea (S1 4.2); cu inventar obligatoriu gestiunea se blocheaza.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseKeeper)]
[Audited("Inv.WarehouseHandover.Start")]
public sealed record StartHandover(int WarehouseId, DateOnly HandoverDate, int ToEmployeeId, string? Notes) : ICommand<InventorySaved>;

/// <summary>Finalizeaza predarea (S1 4.2 pasul 4): perioadele gestionarilor, deblocarea, GestionarSchimbat.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseKeeper)]
[Audited("Inv.WarehouseHandover.Complete")]
public sealed record CompleteHandover(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

/// <summary>Anuleaza o predare in curs; gestionarii raman neschimbati.</summary>
[Module(InventoryModule.Code)]
[RequiresPermission(InventoryPermissions.WarehouseKeeper)]
[Audited("Inv.WarehouseHandover.Cancel")]
public sealed record CancelHandover(int Id, byte[] RowVersion) : ICommand<InventorySaved>;

internal sealed class SaveWarehouseKeeperValidator : AbstractValidator<SaveWarehouseKeeper>
{
    public SaveWarehouseKeeperValidator()
    {
        RuleFor(x => x.Role).InclusiveBetween((byte)1, (byte)2).WithMessage("Alegeți rolul: titular sau înlocuitor.");
        RuleFor(x => x.ValidTo).GreaterThanOrEqualTo(x => x.ValidFrom).When(x => x.ValidTo is not null)
            .WithMessage("Perioada nu se poate încheia înainte să înceapă.");
        RuleFor(x => x.GuaranteeNote).MaximumLength(500).WithMessage("Nota are cel mult 500 de caractere.");
        RuleFor(x => x.RowVersion).NotNull().When(x => x.Id is not null).WithMessage("Lipsește versiunea înregistrării.");
    }
}

internal sealed class StartHandoverValidator : AbstractValidator<StartHandover>
{
    public StartHandoverValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000).WithMessage("Observațiile au cel mult 1000 de caractere.");
    }
}

internal sealed class GetWarehouseKeepersHandler(IWarehouseStore warehouses, IWarehouseKeeperStore store, ICurrentContext context)
    : IQueryHandler<GetWarehouseKeepers, WarehouseKeepersDto>
{
    public async Task<WarehouseKeepersDto> Handle(GetWarehouseKeepers request, CancellationToken cancellationToken)
    {
        await WarehouseAccess.EnsureVisibleAsync(warehouses, context, request.WarehouseId, cancellationToken).ConfigureAwait(false);
        return await store.ListAsync(context.CompanyId, request.WarehouseId, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class SaveWarehouseKeeperHandler(IWarehouseStore warehouses, IWarehouseKeeperStore store, ICurrentContext context, IAuditScope audit)
    : ICommandHandler<SaveWarehouseKeeper, InventorySaved>
{
    public async Task<InventorySaved> Handle(SaveWarehouseKeeper request, CancellationToken cancellationToken)
    {
        await WarehouseAccess.EnsureVisibleAsync(warehouses, context, request.WarehouseId, cancellationToken).ConfigureAwait(false);
        var record = new WarehouseKeeperRecord(request.Id, request.WarehouseId, request.EmployeeId, request.Role, request.ValidFrom, request.ValidTo,
            string.IsNullOrWhiteSpace(request.GuaranteeNote) ? null : request.GuaranteeNote.Trim(), request.RowVersion);
        var saved = await store.SaveKeeperAsync(context.CompanyId, record, context.UserId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", request.WarehouseId, new
        {
            Keeper = saved.Id, record.EmployeeId, record.Role, record.ValidFrom, record.ValidTo, record.GuaranteeNote,
        });
        return saved;
    }
}

internal sealed class HandoverHandler(IWarehouseStore warehouses, IWarehouseKeeperStore store, IOutbox outbox, ICurrentContext context,
    IAuditScope audit) : ICommandHandler<StartHandover, InventorySaved>, ICommandHandler<CompleteHandover, InventorySaved>,
    ICommandHandler<CancelHandover, InventorySaved>
{
    public async Task<InventorySaved> Handle(StartHandover request, CancellationToken cancellationToken)
    {
        await WarehouseAccess.EnsureVisibleAsync(warehouses, context, request.WarehouseId, cancellationToken).ConfigureAwait(false);
        var saved = await store.StartHandoverAsync(context.CompanyId, request.WarehouseId, request.HandoverDate, request.ToEmployeeId,
            string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(), context.UserId, cancellationToken).ConfigureAwait(false);
        await WarehouseEvents.PublishAsync(warehouses, outbox, context.CompanyId, request.WarehouseId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", request.WarehouseId, new { Handover = saved.Id, request.HandoverDate, request.ToEmployeeId });
        return saved;
    }

    public async Task<InventorySaved> Handle(CompleteHandover request, CancellationToken cancellationToken)
    {
        var done = await store.CompleteHandoverAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        await outbox.EnqueueAsync(new GestionarSchimbat(done.WarehouseId, done.FromEmployeeId, done.ToEmployeeId, done.HandoverDate, done.InventoryCountId)
        {
            CompanyId = context.CompanyId,
        }, cancellationToken).ConfigureAwait(false);
        await WarehouseEvents.PublishAsync(warehouses, outbox, context.CompanyId, done.WarehouseId, cancellationToken).ConfigureAwait(false);
        audit.Describe("Inv.Warehouse", done.WarehouseId, new { Handover = done.Id, Completed = true, done.FromEmployeeId, done.ToEmployeeId });
        return new InventorySaved(done.Id, done.RowVersion);
    }

    public async Task<InventorySaved> Handle(CancelHandover request, CancellationToken cancellationToken)
    {
        var saved = await store.CancelHandoverAsync(context.CompanyId, request.Id, request.RowVersion, context.UserId, cancellationToken)
            .ConfigureAwait(false);
        audit.Describe("Inv.WarehouseHandover", saved.Id, new { Cancelled = true });
        return saved;
    }
}
