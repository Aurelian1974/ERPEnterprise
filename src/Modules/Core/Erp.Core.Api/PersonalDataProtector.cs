using System.Text;
using Erp.Core.Application.Partners;
using Microsoft.AspNetCore.DataProtection;

namespace Erp.Core.Api;

/// <summary>
/// CNP-ul partenerilor criptat cu inelul de chei Data Protection al instalarii (08, 5.12): cheile stau in baza, protejate cu
/// certificatul instalarii, deci un backup restaurat cu certificatul ramane lizibil. Scopul e versionat, ca o schimbare de format
/// sa poata coexista cu datele vechi.
/// </summary>
public sealed class PersonalDataProtector(IDataProtectionProvider provider) : IPersonalDataProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("Erp.Core.Partner.PersonalId.v1");

    public byte[] Protect(string value) => protector.Protect(Encoding.UTF8.GetBytes(value));

    public string Unprotect(byte[] value) => Encoding.UTF8.GetString(protector.Unprotect(value));
}
