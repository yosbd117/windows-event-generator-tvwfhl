using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Security.Claims;
using System.DirectoryServices.AccountManagement;
using System.Security.AccessControl;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Eventing.Reader;
using System.Threading;

namespace EventSimulator.Common.Extensions
{
    /// <summary>
    /// Provides thread-safe extension methods for Windows security operations with comprehensive security controls
    /// and audit capabilities. Implements role-based access control and Windows authentication integration.
    /// </summary>
    public static class SecurityExtensions
    {
        private static readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private static readonly SemaphoreSlim _cacheLock = new SemaphoreSlim(1, 1);
        private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole())
                                                            .CreateLogger(typeof(SecurityExtensions));

        /// <summary>
        /// Checks if a WindowsIdentity is in a specified role with caching support.
        /// </summary>
        /// <param name="identity">The Windows identity to check.</param>
        /// <param name="roleName">The role name to verify.</param>
        /// <param name="useCache">Whether to use role membership caching.</param>
        /// <returns>True if the identity is in the specified role, false otherwise.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identity or roleName is null.</exception>
        public static bool IsInRole(this WindowsIdentity identity, string roleName, bool useCache = true)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (string.IsNullOrWhiteSpace(roleName)) throw new ArgumentNullException(nameof(roleName));

            var cacheKey = $"Role_{identity.Name}_{roleName}";

            try
            {
                if (useCache && _cache.TryGetValue(cacheKey, out bool cachedResult))
                {
                    _logger.LogDebug("Retrieved role membership from cache for {Identity} and role {Role}", 
                        identity.Name, roleName);
                    return cachedResult;
                }

                using (var principal = new WindowsPrincipal(identity))
                {
                    var result = principal.IsInRole(roleName);

                    if (useCache)
                    {
                        _cacheLock.Wait();
                        try
                        {
                            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
                        }
                        finally
                        {
                            _cacheLock.Release();
                        }
                    }

                    _logger.LogInformation("Role check completed for {Identity} and role {Role}: {Result}", 
                        identity.Name, roleName, result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking role membership for {Identity} and role {Role}", 
                    identity.Name, roleName);
                throw;
            }
        }

        /// <summary>
        /// Retrieves all Windows groups for an identity with caching and performance optimizations.
        /// </summary>
        /// <param name="identity">The Windows identity to check.</param>
        /// <param name="cacheExpiration">Cache expiration timespan.</param>
        /// <returns>Collection of Windows group names with security context.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identity is null.</exception>
        public static IEnumerable<string> GetWindowsGroups(this WindowsIdentity identity, 
            TimeSpan cacheExpiration = default)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (cacheExpiration == default) cacheExpiration = TimeSpan.FromHours(1);

            var cacheKey = $"Groups_{identity.Name}";

            try
            {
                if (_cache.TryGetValue(cacheKey, out IEnumerable<string> cachedGroups))
                {
                    _logger.LogDebug("Retrieved groups from cache for {Identity}", identity.Name);
                    return cachedGroups;
                }

                using (var context = new PrincipalContext(ContextType.Domain))
                using (var userPrincipal = UserPrincipal.FindByIdentity(context, identity.Name))
                {
                    if (userPrincipal == null)
                    {
                        _logger.LogWarning("User principal not found for {Identity}", identity.Name);
                        return Array.Empty<string>();
                    }

                    var groups = new List<string>();
                    var groupPrincipals = userPrincipal.GetGroups();

                    foreach (var group in groupPrincipals)
                    {
                        groups.Add(group.Name);
                        group.Dispose();
                    }

                    _cacheLock.Wait();
                    try
                    {
                        _cache.Set(cacheKey, groups, cacheExpiration);
                    }
                    finally
                    {
                        _cacheLock.Release();
                    }

                    _logger.LogInformation("Retrieved {Count} groups for {Identity}", 
                        groups.Count, identity.Name);
                    return groups;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving Windows groups for {Identity}", identity.Name);
                throw;
            }
        }

        /// <summary>
        /// Checks if identity has a specific Windows privilege with comprehensive validation.
        /// </summary>
        /// <param name="identity">The Windows identity to check.</param>
        /// <param name="privilegeName">The privilege name to verify.</param>
        /// <param name="auditCheck">Whether to create an audit trail for the check.</param>
        /// <returns>True if the identity has the specified privilege.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identity or privilegeName is null.</exception>
        public static bool HasRequiredPrivilege(this WindowsIdentity identity, string privilegeName, 
            bool auditCheck = true)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (string.IsNullOrWhiteSpace(privilegeName)) 
                throw new ArgumentNullException(nameof(privilegeName));

            try
            {
                using (var principal = new WindowsPrincipal(identity))
                {
                    var hasPrivilege = principal.Claims.Any(c => 
                        c.Type == ClaimTypes.WindowsUserClaim && 
                        c.Value.Equals(privilegeName, StringComparison.OrdinalIgnoreCase));

                    if (auditCheck)
                    {
                        _logger.LogInformation(
                            "Privilege check for {Identity} and privilege {Privilege}: {Result}", 
                            identity.Name, privilegeName, hasPrivilege);
                    }

                    return hasPrivilege;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking privilege for {Identity} and privilege {Privilege}", 
                    identity.Name, privilegeName);
                throw;
            }
        }

        /// <summary>
        /// Gets effective permissions for an identity on event log with security validation.
        /// </summary>
        /// <param name="identity">The Windows identity to check.</param>
        /// <param name="eventLogName">The name of the event log.</param>
        /// <param name="includeInherited">Whether to include inherited permissions.</param>
        /// <returns>Effective permissions on the event log.</returns>
        /// <exception cref="ArgumentNullException">Thrown when identity or eventLogName is null.</exception>
        public static EventLogRights GetEffectivePermissions(this WindowsIdentity identity, 
            string eventLogName, bool includeInherited = true)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (string.IsNullOrWhiteSpace(eventLogName)) 
                throw new ArgumentNullException(nameof(eventLogName));

            try
            {
                using (var eventLog = new EventLogConfiguration(eventLogName))
                {
                    var security = eventLog.GetSecurityDescriptor();
                    var rules = security.GetAccessRules(includeInherited, includeInherited, 
                        typeof(SecurityIdentifier));

                    var rights = EventLogRights.None;

                    foreach (EventLogAccessRule rule in rules)
                    {
                        if (identity.User.Equals(rule.IdentityReference) || 
                            identity.Groups.Contains(rule.IdentityReference))
                        {
                            if (rule.AccessControlType == AccessControlType.Allow)
                            {
                                rights |= rule.EventLogRights;
                            }
                            else
                            {
                                rights &= ~rule.EventLogRights;
                            }
                        }
                    }

                    _logger.LogInformation(
                        "Retrieved effective permissions for {Identity} on event log {EventLog}: {Rights}", 
                        identity.Name, eventLogName, rights);

                    return rights;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Error getting effective permissions for {Identity} on event log {EventLog}", 
                    identity.Name, eventLogName);
                throw;
            }
        }
    }
}