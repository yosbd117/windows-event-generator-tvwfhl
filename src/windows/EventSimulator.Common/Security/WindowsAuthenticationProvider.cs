using System;
using System.Security.Principal;
using System.Security.Claims;
using System.DirectoryServices.AccountManagement; // v6.0.0
using System.Threading.Tasks;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using EventSimulator.Common.Configuration;
using EventSimulator.Common.Extensions;

namespace EventSimulator.Common.Security
{
    /// <summary>
    /// Provides thread-safe Windows authentication and authorization services with enhanced security controls
    /// and performance optimization for the Windows Event Simulator application.
    /// </summary>
    public sealed class WindowsAuthenticationProvider : IDisposable
    {
        private readonly AppSettings _appSettings;
        private readonly ILogger<WindowsAuthenticationProvider> _logger;
        private readonly ISecurityTokenManager _tokenManager;
        private readonly object _lockObject = new object();
        private readonly ConcurrentDictionary<string, SecurityToken> _tokenCache;
        private WindowsIdentity _currentIdentity;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the WindowsAuthenticationProvider with dependency injection
        /// and security token management.
        /// </summary>
        /// <param name="appSettings">Application configuration settings</param>
        /// <param name="logger">Logging service</param>
        /// <param name="tokenManager">Security token management service</param>
        /// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
        public WindowsAuthenticationProvider(
            AppSettings appSettings,
            ILogger<WindowsAuthenticationProvider> logger,
            ISecurityTokenManager tokenManager)
        {
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokenManager = tokenManager ?? throw new ArgumentNullException(nameof(tokenManager));
            _tokenCache = new ConcurrentDictionary<string, SecurityToken>();

            _logger.LogInformation("WindowsAuthenticationProvider initialized with Windows authentication {enabled}",
                _appSettings.UseWindowsAuthentication);
        }

        /// <summary>
        /// Authenticates a user using Windows authentication with token management and security validation.
        /// </summary>
        /// <returns>Authentication result with security token</returns>
        /// <exception cref="SecurityException">Thrown when authentication fails</exception>
        public async Task<AuthenticationResult> AuthenticateUser()
        {
            if (!_appSettings.UseWindowsAuthentication)
            {
                _logger.LogError("Windows authentication is disabled in configuration");
                throw new SecurityException("Windows authentication is not enabled");
            }

            try
            {
                lock (_lockObject)
                {
                    _currentIdentity = WindowsIdentity.GetCurrent();
                    if (_currentIdentity == null || !_currentIdentity.IsAuthenticated)
                    {
                        _logger.LogError("Failed to obtain authenticated Windows identity");
                        throw new SecurityException("Invalid Windows identity");
                    }
                }

                var token = await _tokenManager.GenerateSecurityToken(_currentIdentity);
                if (!_tokenCache.TryAdd(token.TokenId, token))
                {
                    _logger.LogWarning("Token cache addition failed for user {user}", _currentIdentity.Name);
                }

                _logger.LogInformation("User {user} successfully authenticated", _currentIdentity.Name);

                return new AuthenticationResult
                {
                    IsAuthenticated = true,
                    UserName = _currentIdentity.Name,
                    SecurityToken = token,
                    Groups = _currentIdentity.GetWindowsGroups()
                };
            }
            catch (Exception ex) when (ex is not SecurityException)
            {
                _logger.LogError(ex, "Authentication failed for Windows identity");
                throw new SecurityException("Authentication failed", ex);
            }
        }

        /// <summary>
        /// Checks if current user is in specified Windows role with token validation.
        /// </summary>
        /// <param name="roleName">Role name to verify</param>
        /// <param name="securityToken">Security token for validation</param>
        /// <returns>True if user is in role and token is valid</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null</exception>
        /// <exception cref="SecurityException">Thrown when token is invalid</exception>
        public async Task<bool> IsInRole(string roleName, string securityToken)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentNullException(nameof(roleName));
            if (string.IsNullOrWhiteSpace(securityToken))
                throw new ArgumentNullException(nameof(securityToken));

            try
            {
                if (!await ValidateSecurityToken(securityToken))
                {
                    _logger.LogWarning("Invalid security token provided for role check");
                    throw new SecurityException("Invalid security token");
                }

                lock (_lockObject)
                {
                    if (_currentIdentity == null || !_currentIdentity.IsAuthenticated)
                    {
                        _logger.LogError("No authenticated identity available for role check");
                        return false;
                    }

                    var isInRole = _currentIdentity.IsInRole(roleName);
                    _logger.LogInformation("Role check for {user} and role {role}: {result}",
                        _currentIdentity.Name, roleName, isInRole);
                    return isInRole;
                }
            }
            catch (Exception ex) when (ex is not SecurityException)
            {
                _logger.LogError(ex, "Role check failed for role {role}", roleName);
                throw new SecurityException($"Role check failed for {roleName}", ex);
            }
        }

        /// <summary>
        /// Verifies if user has required Event Log access privileges with enhanced security.
        /// </summary>
        /// <param name="eventLogName">Event log name to check</param>
        /// <param name="securityToken">Security token for validation</param>
        /// <returns>True if user has required access and valid token</returns>
        /// <exception cref="ArgumentNullException">Thrown when parameters are null</exception>
        /// <exception cref="SecurityException">Thrown when token is invalid</exception>
        public async Task<bool> HasEventLogAccess(string eventLogName, string securityToken)
        {
            if (string.IsNullOrWhiteSpace(eventLogName))
                throw new ArgumentNullException(nameof(eventLogName));
            if (string.IsNullOrWhiteSpace(securityToken))
                throw new ArgumentNullException(nameof(securityToken));

            try
            {
                if (!await ValidateSecurityToken(securityToken))
                {
                    _logger.LogWarning("Invalid security token provided for event log access check");
                    throw new SecurityException("Invalid security token");
                }

                lock (_lockObject)
                {
                    if (_currentIdentity == null || !_currentIdentity.IsAuthenticated)
                    {
                        _logger.LogError("No authenticated identity available for event log access check");
                        return false;
                    }

                    var rights = _currentIdentity.GetEffectivePermissions(eventLogName);
                    var hasAccess = (rights & System.Diagnostics.Eventing.Reader.EventLogRights.ReadEvents) != 0;

                    _logger.LogInformation("Event log access check for {user} and log {log}: {result}",
                        _currentIdentity.Name, eventLogName, hasAccess);
                    return hasAccess;
                }
            }
            catch (Exception ex) when (ex is not SecurityException)
            {
                _logger.LogError(ex, "Event log access check failed for log {log}", eventLogName);
                throw new SecurityException($"Event log access check failed for {eventLogName}", ex);
            }
        }

        private async Task<bool> ValidateSecurityToken(string token)
        {
            try
            {
                if (_tokenCache.TryGetValue(token, out var cachedToken))
                {
                    return await _tokenManager.ValidateToken(cachedToken);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Token validation failed");
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _currentIdentity?.Dispose();
                _tokenCache.Clear();
                _disposed = true;
            }
        }
    }

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
        public SecurityException(string message, Exception innerException) : base(message, innerException) { }
    }
}