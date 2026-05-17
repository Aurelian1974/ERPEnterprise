namespace Administration.Application.Abstractions;

public sealed record AnafAdresaSediuSocial(
    string? Strada,
    string? Numar,
    string? Localitate,
    string? Judet,
    string? CodPostal,
    string? Tara
);

public sealed record AnafCompanyData(
    string Denumire,
    int Cui,
    string? NrRegCom,
    string? Adresa,
    bool ScpTva,
    string? DataInregistrarii,
    string? StareInregistrare,
    string? Telefon,
    string? FormaJuridica,
    AnafAdresaSediuSocial? AdresaSediuSocial
);

public sealed class AnafVerificationResult
{
    public bool IsSuccess { get; private init; }
    public string? ErrorMessage { get; private init; }
    public AnafCompanyData? Data { get; private init; }

    public static AnafVerificationResult Success(AnafCompanyData data) =>
        new() { IsSuccess = true, Data = data };

    public static AnafVerificationResult Failure(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}

public interface IAnafService
{
    Task<AnafVerificationResult> VerifyAsync(string cui, CancellationToken ct = default);
}
