using System.Text;
using Erp.Finance.Application.Treasury;
using Microsoft.AspNetCore.DataProtection;

namespace Erp.Finance.Api;

/// <summary>
/// Actul de identitate al persoanelor fara fisa de partener de pe documentele de casa, criptat cu inelul de chei Data Protection al
/// instalarii (SP-20; 08, 5.12), ca CNP-ul partenerilor in Nucleu. Scopul e versionat.
/// </summary>
public sealed class PersonIdProtector(IDataProtectionProvider provider) : IPersonIdProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Erp.Finance.CashDocument.PersonId.v1");

    public byte[] Protect(string value) => protector.Protect(Encoding.UTF8.GetBytes(value));
}
