using Azure.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client.Extensibility;
using SkiaSharp;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Security
{
    public class SignInManagerOptions
    {
        public int MaxNonces { get; set; } = 16;
    }

    public class SignInManagerSavedOptions
    {
        private StrongBox<bool>? _hasPassword;
        private StrongBox<(string Salt, string Hash)>? _securityStamp;

        public string PasswordSalt { get; set; } = "";
        public string PasswordHash { get; set; } = "";

        public bool HasPassword
        {
            get
            {
                var res = _hasPassword;
                if (res == null)
                    _hasPassword = res = new StrongBox<bool>(!PasswordHash.Equals(SignInManager.GetPasswordHash(PasswordSalt, ""), StringComparison.OrdinalIgnoreCase));
                return res.Value;
            }
        }

        public SignInManagerSavedOptions Clone()
            => (SignInManagerSavedOptions)MemberwiseClone();

        public string GetSecurityStamp(string securityStampSalt)
        {
            var res = _securityStamp;
            if (res == null || res.Value.Salt != securityStampSalt)
                _securityStamp = res = new StrongBox<(string Salt, string Hash)>((securityStampSalt, SignInManager.GetPasswordHash(securityStampSalt, PasswordHash)));
            return res.Value.Hash;
        }
    }

    public class SignInManager : ISecurityStampValidator, ISignInManager, IAuthorizationEvaluator, IConstructable
    {
        private const string SecurityStampClaimType = "SecurityStampClaimType";
        private readonly ILogger<SignInManager> _logger;
        private readonly IOptionsMonitor<SignInManagerOptions> _options;
        private readonly IOptionsWriter<SignInManagerSavedOptions> _savedOptionsWriter;
        private readonly Dictionary<string, SystemTimestamp> _clientNonces;
        private readonly IHttpContextAccessor _contextAccessor;
        private readonly string _securityStamp;

        public AsyncEvent PasswordChangedEvent { get; } = new();

        private HttpContext HttpContext
        {
            get
            {
                var httpContext = _contextAccessor.HttpContext;
                if (httpContext == null)
                    throw new InvalidOperationException("HttpContext should be set");
                return httpContext;
            }
        }

        public bool HasPassword => _savedOptionsWriter.CurrentValue.HasPassword;

        public SignInManager(
            ILogger<SignInManager> logger,
            IOptionsMonitor<SignInManagerOptions> options,
            IOptionsWriter<SignInManagerSavedOptions> savedOptionsWriter,
            IHttpContextAccessor contextAccessor, 
            IAuthenticationSchemeProvider schemes)
        {
            _logger = logger;
            _options = options;
            _savedOptionsWriter = savedOptionsWriter;
            _contextAccessor = contextAccessor;
            _securityStamp = GetRandomString();
            _clientNonces = new();
        }

        public async ValueTask Construct(CancellationToken cancel = default)
        {
            var savedOptions = _savedOptionsWriter.CurrentValue;
            if (string.IsNullOrEmpty(savedOptions.PasswordSalt))
            {
                // initialize empty password
                savedOptions = savedOptions.Clone();
                savedOptions.PasswordSalt = GetRandomString();
                savedOptions.PasswordHash = GetPasswordHash(savedOptions.PasswordSalt, "");
                await _savedOptionsWriter.Write(savedOptions, cancel);
            }
        }

        private static string GetRandomString()
            => RandomNumberGenerator.GetHexString(64);

        public Task ValidateAsync(CookieValidatePrincipalContext context)
        {
            var securityStamp = context.Principal?.FindFirstValue(SecurityStampClaimType);
            if (string.IsNullOrEmpty(securityStamp))
                context.RejectPrincipal();
            else
            {
                var savedOptions = _savedOptionsWriter.CurrentValue;
                if (securityStamp.Equals(_securityStamp, StringComparison.OrdinalIgnoreCase) != true)
                    context.RejectPrincipal();
            }
            return Task.CompletedTask;
        }

        public async Task SignOut(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var httpContext = HttpContext;
            _logger.LogInformation($"Signing out for {Dump(httpContext)}");
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        public async Task SignIn(CancellationToken cancel)
        { 
            await SignOut(cancel);
            var httpContext = HttpContext;
            _logger.LogInformation($"Signing in for {Dump(httpContext)}");
            var identity = new ClaimsIdentity(GetType().FullName);
            var savedOptions = _savedOptionsWriter.CurrentValue;
            identity.AddClaim(new Claim(SecurityStampClaimType, _securityStamp));
            var userPrincipal = new ClaimsPrincipal(identity);
            await httpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                userPrincipal,
                new AuthenticationProperties());
            httpContext.User = userPrincipal;
        }

        public Task<string> GetServerNonce(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var savedOptions = _savedOptionsWriter.CurrentValue;
            return Task.FromResult(savedOptions.PasswordSalt);
        }

        public Task<string> GetClientNonce(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            var nonce = GetRandomString();
            var httpContext = HttpContext;
            _logger.LogDebug($"Generating nonce for {Dump(httpContext)}");
            lock (_clientNonces)
            {
                _clientNonces[nonce] = SystemTimestamp.Now;
                if (_clientNonces.Count > options.MaxNonces)
                {
                    foreach (var pair in _clientNonces.OrderBy(x => x.Value))
                    {
                        _clientNonces.Remove(pair.Key);
                        if (_clientNonces.Count <= options.MaxNonces)
                            break;
                    }
                }
            }
            return Task.FromResult(nonce);
        }

        public async Task<bool> TrySignInUsingPassword(string password, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            var httpContext = HttpContext;
            _logger.LogInformation($"Trying to password log in for {Dump(httpContext)}");
            var savedOptions = _savedOptionsWriter.CurrentValue;
            var hash = GetPasswordHash(savedOptions.PasswordSalt, password);
            if (hash != savedOptions.PasswordHash)
                return false;
            await SignIn(cancel);
            return true;
        }

        public static string GetPasswordHash(string salt, string? password)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}")));

        public async Task<bool> TrySignInUsingHash(string clientNonce, string hash, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            var httpContext = HttpContext;
            _logger.LogInformation($"Trying to hash log in for {Dump(httpContext)}");

            lock (_clientNonces)
            {
                if (!_clientNonces.Remove(clientNonce))
                    return false;
            }
            var savedOptions = _savedOptionsWriter.CurrentValue;
            var hash2 = GetPasswordHash(clientNonce, savedOptions.PasswordHash);
            if (!hash2.Equals(hash, StringComparison.OrdinalIgnoreCase))
                return false;
            await SignIn(cancel);
            return true;
        }

        public async Task SetPassword(string? password, CancellationToken cancel = default)
        {
            _logger.LogInformation($"Setting new password");
            var clone = _savedOptionsWriter.CurrentValue.Clone();
            clone.PasswordSalt = GetRandomString();
            clone.PasswordHash = GetPasswordHash(clone.PasswordSalt, password ?? "");
            await _savedOptionsWriter.Write(clone, cancel);
            await PasswordChangedEvent.Invoke(cancel);
        }

        private static string Dump(HttpContext context)
            => new
            {
                Connection = new 
                {
                    context.Connection.Id,
                    context.Connection.RemoteIpAddress,
                    context.Connection.RemotePort,
                    context.Connection.LocalPort,
                    ClientCertificate = context.Connection.ClientCertificate?.Thumbprint,
                },
                RequestHeaders = string.Join("; ", context.Request.Headers),
            }.ToString()!;

        public AuthorizationResult Evaluate(AuthorizationHandlerContext context)
        {
            if (!HasPassword || context.User.Identity?.IsAuthenticated == true)
                return AuthorizationResult.Success();
            else
                return AuthorizationResult.Failed();
        }
    }
}
