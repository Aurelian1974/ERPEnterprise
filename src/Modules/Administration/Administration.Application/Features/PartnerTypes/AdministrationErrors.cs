using Shared.Kernel.Errors;

namespace Administration.Application.Features.PartnerTypes;

public static class AdministrationErrors
{
    public static Error PartnerTypeNotFound(byte id) =>
        new("Administration.PartnerTypeNotFound", $"Tipul de partener cu ID-ul {id} nu a fost găsit.");

    public static Error PartnerNotFound(Guid id) =>
        new("Administration.PartnerNotFound", $"Partenerul cu ID-ul {id} nu a fost găsit.");

    public static Error PartnerCodeAlreadyExists(string code) =>
        new("Administration.PartnerCodeAlreadyExists", $"Codul '{code}' este deja folosit de un alt partener.");

    public static Error PartnerCuiMissing(Guid id) =>
        new("Administration.PartnerCuiMissing", $"Partenerul cu ID-ul {id} nu are CUI configurat. Completați CUI-ul înainte de verificarea ANAF.");

    public static Error AnafVerificationFailed(string message) =>
        new("Administration.AnafVerificationFailed", $"Verificarea ANAF a eșuat: {message}");
}
