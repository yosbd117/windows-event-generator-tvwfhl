using System;
using System.Security.Principal;
using System.Security.AccessControl;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Threading;
using EventSimulator.Common.Security;
using EventSimulator.Core.Constants;

namespace EventSimulator.Core.Utils
{
    /// <summary>
    /// Provides comprehensive security utility functions for Windows Event Log operations with thread-safe
    /// access validation, granular privilege management, and detailed audit logging capabilities.
    /// </summary>
    public static class SecurityUtils
    {
        // Constants for required event log access rights
        private const int EVENT_LOG_REQUIRED_RIGHTS = 0x0010 | 0x0002 | 0x0008; // Read, Write, Clear
        private const int SECURITY_DESCRIPTOR_REVISION = 1;
        private const int MAX_AUDIT_RETRY_COUNT = 3;

        private static readonly SemaphoreSlim _securityLock = new SemaphoreSlim(1, 1);
        private static readonly WindowsAuthenticationProvider _authProvider;

        static SecurityUtils()
        {
            _authProvider = new WindowsAuthenticationProvider(
                new Common.Configuration.AppSettings(),
                LoggerFactory.Create(builder => builder.AddConsole())
                    .CreateLogger<WindowsAuthenticationProvider>(),
                new SecurityTokenManager());
        }

        /// <summary>
        /// Validates if the current user has required access rights to the specified event log
        /// with comprehensive security checks and audit logging.
        /// </summary>
        /// <param name="eventLogName">The name of the event log to validate access for</param>
        /// <param name="requiredRights">The specific access rights required</param>
        /// <returns>True if access is granted, false otherwise</returns>
        /// <exception cref="ArgumentNullException">Thrown when eventLogName is null or empty</exception>
        /// <exception cref="SecurityException">Thrown when security validation fails</exception>
        public static async Task<bool> ValidateEventLogAccess(string eventLogName, AccessRights requiredRights)
        {
            if (string.IsNullOrWhiteSpace(eventLogName))
                throw new ArgumentNullException(nameof(eventLogName));

            await _securityLock.WaitAsync();
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                if (identity == null || !identity.IsAuthenticated)
                {
                    await GenerateAuditEvent("EventLogAccess", false, 
                        new AuditDetail { Operation = "AccessValidation", Target = eventLogName });
                    return false;
                }

                var securityToken = await _authProvider.AuthenticateUser();
                var hasAccess = await _authProvider.HasEventLogAccess(eventLogName, securityToken.SecurityToken.TokenId);

                if (hasAccess)
                {
                    var validationResult = await ValidatePrivileges(new SecurityContext 
                    { 
                        Identity = identity,
                        RequiredRights = requiredRights,
                        EventLogName = eventLogName
                    });

                    await GenerateAuditEvent("EventLogAccess", validationResult.IsValid,
                        new AuditDetail 
                        { 
                            Operation = "AccessValidation",
                            Target = eventLogName,
                            Result = validationResult.IsValid ? "Granted" : "Denied",
                            User = identity.Name
                        });

                    return validationResult.IsValid;
                }

                return false;
            }
            catch (Exception ex)
            {
                await GenerateAuditEvent("EventLogAccess", false,
                    new AuditDetail 
                    { 
                        Operation = "AccessValidation",
                        Target = eventLogName,
                        Error = ex.Message
                    });
                throw new SecurityException("Failed to validate event log access", ex);
            }
            finally
            {
                _securityLock.Release();
            }
        }

        /// <summary>
        /// Creates a comprehensive security descriptor for event log access control with
        /// granular permissions and audit settings.
        /// </summary>
        /// <param name="accountName">The account name to grant access to</param>
        /// <param name="rights">The specific rights to grant</param>
        /// <param name="options">Additional security options</param>
        /// <returns>A configured security descriptor</returns>
        public static SecurityDescriptor CreateSecurityDescriptor(string accountName, AccessRights rights, SecurityOptions options)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                throw new ArgumentNullException(nameof(accountName));

            var securityDescriptor = new SecurityDescriptor();
            securityDescriptor.SetSecurityDescriptorControl(
                SecurityDescriptorControl.DiscretionaryAclPresent |
                SecurityDescriptorControl.SystemAclPresent,
                SecurityDescriptorControl.DiscretionaryAclPresent |
                SecurityDescriptorControl.SystemAclPresent);

            var accessRule = new EventLogAccessRule(
                accountName,
                rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow);

            var discretionaryAcl = new DiscretionaryAcl(
                false, 
                false, 
                SECURITY_DESCRIPTOR_REVISION);
            discretionaryAcl.AddAccess(
                AccessControlType.Allow,
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                (int)rights,
                InheritanceFlags.None,
                PropagationFlags.None);

            securityDescriptor.DiscretionaryAcl = discretionaryAcl;

            if (options.EnableAuditing)
            {
                var systemAcl = new SystemAcl(
                    false, 
                    false, 
                    SECURITY_DESCRIPTOR_REVISION);
                systemAcl.AddAudit(
                    accountName,
                    (int)rights,
                    false,
                    AuditFlags.Success | AuditFlags.Failure);
                securityDescriptor.SystemAcl = systemAcl;
            }

            return securityDescriptor;
        }

        /// <summary>
        /// Validates if current user has required privileges for event generation with
        /// comprehensive role-based checks.
        /// </summary>
        /// <param name="context">The security context containing validation requirements</param>
        /// <returns>Detailed validation result with privilege status</returns>
        public static async Task<ValidationResult> ValidatePrivileges(SecurityContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            try
            {
                var result = new ValidationResult { IsValid = false };
                var securityToken = await _authProvider.AuthenticateUser();

                var hasRequiredRole = await _authProvider.IsInRole(
                    "EventGenerators", 
                    securityToken.SecurityToken.TokenId);

                if (!hasRequiredRole)
                {
                    result.Message = "User lacks required role membership";
                    return result;
                }

                if (context.RequiredRights.HasFlag(AccessRights.Write))
                {
                    var hasWriteAccess = await _authProvider.IsInRole(
                        "EventLogWriters",
                        securityToken.SecurityToken.TokenId);

                    if (!hasWriteAccess)
                    {
                        result.Message = "User lacks write privileges";
                        return result;
                    }
                }

                result.IsValid = true;
                return result;
            }
            catch (Exception ex)
            {
                throw new SecurityException("Failed to validate privileges", ex);
            }
        }

        /// <summary>
        /// Generates detailed audit events for security-related operations with retry logic
        /// and comprehensive logging.
        /// </summary>
        /// <param name="operation">The operation being audited</param>
        /// <param name="success">Whether the operation succeeded</param>
        /// <param name="details">Additional audit details</param>
        public static async Task GenerateAuditEvent(string operation, bool success, AuditDetail details)
        {
            if (string.IsNullOrWhiteSpace(operation))
                throw new ArgumentNullException(nameof(operation));

            var retryCount = 0;
            while (retryCount < MAX_AUDIT_RETRY_COUNT)
            {
                try
                {
                    var eventId = success ? SecurityEventIds.AuditSuccess : SecurityEventIds.AuditFailure;
                    var identity = WindowsIdentity.GetCurrent();

                    var auditEvent = new EventLogEntry
                    {
                        EventId = eventId,
                        Source = "WindowsEventSimulator",
                        TimeGenerated = DateTime.UtcNow,
                        EntryType = success ? EventLogEntryType.Information : EventLogEntryType.Warning,
                        Message = $"Operation: {operation}\n" +
                                $"User: {identity?.Name ?? "Unknown"}\n" +
                                $"Target: {details.Target}\n" +
                                $"Result: {details.Result}\n" +
                                $"Error: {details.Error}"
                    };

                    using (var eventLog = new EventLog("Security"))
                    {
                        eventLog.WriteEntry(
                            auditEvent.Message,
                            auditEvent.EntryType,
                            auditEvent.EventId);
                    }

                    break;
                }
                catch (Exception) when (retryCount < MAX_AUDIT_RETRY_COUNT - 1)
                {
                    retryCount++;
                    await Task.Delay(100 * retryCount);
                }
            }
        }
    }

    public class SecurityContext
    {
        public WindowsIdentity Identity { get; set; }
        public AccessRights RequiredRights { get; set; }
        public string EventLogName { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
    }

    public class AuditDetail
    {
        public string Operation { get; set; }
        public string Target { get; set; }
        public string Result { get; set; }
        public string Error { get; set; }
        public string User { get; set; }
    }
}