using SLS4All.Compact.Threading;

namespace SLS4All.Compact.Security
{
    public interface ISignInManager
    {
        AsyncEvent PasswordChangedEvent { get; }
        bool HasPassword { get; }
        Task SignOut(CancellationToken cancel = default);
        Task SignIn(CancellationToken cancel = default);
        Task<bool> TrySignInUsingPassword(string password, CancellationToken cancel = default);
        Task<bool> TrySignInUsingHash(string clientNonce, string hash, CancellationToken cancel = default);
        Task<string> GetServerNonce(CancellationToken cancel);
        Task<string> GetClientNonce(CancellationToken cancel);
        Task SetPassword(string? password, CancellationToken cancel = default);
    }
}