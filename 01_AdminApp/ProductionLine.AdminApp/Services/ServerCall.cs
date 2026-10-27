using System;
using System.Threading.Tasks;
namespace _01_AdminApp.Services;

// Supplied client discards HTTP status codes. This exact message is the supplied
// server's Unauthorized() response. Do not classify unrelated failures as expiry.
public static class ServerCall
{
    public static event Action? SessionExpired;
    public static async Task<T> Run<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (InvalidOperationException ex) when (ex.Message == "로그인이 필요합니다.")
        {
            SessionExpired?.Invoke();
            throw;
        }
    }
}
