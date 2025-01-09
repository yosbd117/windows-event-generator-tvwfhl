using System;

namespace EventSimulator.Core.Constants
{
    /// <summary>
    /// Provides a comprehensive collection of Windows Security Event Log IDs organized by security categories.
    /// Each constant represents an official Windows Event ID and includes references to relevant MITRE ATT&CK techniques.
    /// </summary>
    public static class SecurityEventIds
    {
        #region Authentication Events
        /// <summary>
        /// Credential validation was attempted
        /// MITRE ATT&CK: T1110 - Brute Force
        /// </summary>
        public const int AccountLogon = 4776;

        /// <summary>
        /// An account was logged off
        /// MITRE ATT&CK: T1078 - Valid Accounts
        /// </summary>
        public const int AccountLogoff = 4634;

        /// <summary>
        /// A user account was locked out
        /// MITRE ATT&CK: T1110 - Brute Force
        /// </summary>
        public const int AccountLockout = 4740;

        /// <summary>
        /// Credential validation
        /// MITRE ATT&CK: T1110 - Brute Force
        /// </summary>
        public const int CredentialValidation = 4774;

        /// <summary>
        /// An account was successfully logged on
        /// MITRE ATT&CK: T1078 - Valid Accounts
        /// </summary>
        public const int LogonSuccess = 4624;

        /// <summary>
        /// An account failed to log on
        /// MITRE ATT&CK: T1110 - Brute Force
        /// </summary>
        public const int LogonFailure = 4625;

        /// <summary>
        /// User initiated logoff
        /// MITRE ATT&CK: T1078 - Valid Accounts
        /// </summary>
        public const int LogoffSuccess = 4647;
        #endregion

        #region Privilege Events
        /// <summary>
        /// Special privileges assigned to new logon
        /// MITRE ATT&CK: T1078.003 - Local Accounts
        /// </summary>
        public const int SpecialPrivilegeAssigned = 4672;

        /// <summary>
        /// A privileged service was called
        /// MITRE ATT&CK: T1134 - Access Token Manipulation
        /// </summary>
        public const int PrivilegeUsed = 4673;
        #endregion

        #region Process Tracking
        /// <summary>
        /// A new process has been created
        /// MITRE ATT&CK: T1059 - Command and Scripting Interpreter
        /// </summary>
        public const int ProcessCreation = 4688;

        /// <summary>
        /// A process has exited
        /// MITRE ATT&CK: T1059 - Command and Scripting Interpreter
        /// </summary>
        public const int ProcessTermination = 4689;
        #endregion

        #region Object Access
        /// <summary>
        /// A handle to an object was requested
        /// MITRE ATT&CK: T1069 - Permission Groups Discovery
        /// </summary>
        public const int ObjectAccess = 4656;

        /// <summary>
        /// An attempt was made to access an object
        /// MITRE ATT&CK: T1069 - Permission Groups Discovery
        /// </summary>
        public const int ObjectAccessDenied = 4663;
        #endregion

        #region Policy Changes
        /// <summary>
        /// System audit policy was changed
        /// MITRE ATT&CK: T1562.002 - Disable Windows Event Logging
        /// </summary>
        public const int PolicyChange = 4719;

        /// <summary>
        /// System audit policy was changed
        /// MITRE ATT&CK: T1562.002 - Disable Windows Event Logging
        /// </summary>
        public const int AuditPolicyChange = 4719;
        #endregion

        #region Account Management
        /// <summary>
        /// A user account was created
        /// MITRE ATT&CK: T1136 - Create Account
        /// </summary>
        public const int UserAccountCreated = 4720;

        /// <summary>
        /// A user account was changed
        /// MITRE ATT&CK: T1098 - Account Manipulation
        /// </summary>
        public const int UserAccountChanged = 4738;

        /// <summary>
        /// A user account was deleted
        /// MITRE ATT&CK: T1531 - Account Access Removal
        /// </summary>
        public const int UserAccountDeleted = 4726;

        /// <summary>
        /// A security-enabled local group was created
        /// MITRE ATT&CK: T1136 - Create Account
        /// </summary>
        public const int SecurityGroupCreated = 4731;

        /// <summary>
        /// A security-enabled local group was changed
        /// MITRE ATT&CK: T1098 - Account Manipulation
        /// </summary>
        public const int SecurityGroupChanged = 4735;

        /// <summary>
        /// A security-enabled local group was deleted
        /// MITRE ATT&CK: T1531 - Account Access Removal
        /// </summary>
        public const int SecurityGroupDeleted = 4734;
        #endregion
    }
}