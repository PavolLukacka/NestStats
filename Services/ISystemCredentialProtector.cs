namespace NestStats2.Services;

public interface ISystemCredentialProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
