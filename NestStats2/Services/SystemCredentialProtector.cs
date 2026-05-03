using Microsoft.AspNetCore.DataProtection;

namespace NestStats2.Services;

public sealed class SystemCredentialProtector : ISystemCredentialProtector
{
    private readonly IDataProtector _protector;

    public SystemCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("NestStats2.SystemCredentialProtector.v1");
    }

    public string Protect(string plainText)
    {
        return _protector.Protect(plainText);
    }

    public string Unprotect(string protectedText)
    {
        return _protector.Unprotect(protectedText);
    }
}
