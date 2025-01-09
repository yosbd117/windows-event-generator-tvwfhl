# Windows Event Simulator

[![Build Status](https://github.com/windows-event-simulator/actions/workflows/build.yml/badge.svg)](https://github.com/windows-event-simulator/actions/workflows/build.yml)
[![Code Quality](https://github.com/windows-event-simulator/actions/workflows/codeql.yml/badge.svg)](https://github.com/windows-event-simulator/actions/workflows/codeql.yml)
[![Release](https://github.com/windows-event-simulator/actions/workflows/release.yml/badge.svg)](https://github.com/windows-event-simulator/actions/workflows/release.yml)
[![Test Coverage](https://github.com/windows-event-simulator/actions/workflows/coverage.yml/badge.svg)](https://github.com/windows-event-simulator/actions/workflows/coverage.yml)

A specialized security testing tool for generating synthetic Windows Event Log entries that accurately replicate real-world security incidents and system behaviors. This tool enables security professionals to test detection mechanisms, validate security controls, and conduct training in a controlled environment without impacting production systems.

## Features

- High-performance Event Generation Engine supporting Security, System, and Application logs
- Advanced Template Management with MITRE ATT&CK mapping
- Scenario Builder with timeline-based sequencing and conditional triggers
- Native Windows Event Log API integration
- SIEM system export with multiple format support
- Role-based access control with granular permissions
- Comprehensive audit logging and monitoring
- Performance optimization with configurable thread count

## Prerequisites

### Operating System
- Windows 10 Pro or Enterprise (21H2 or later)
- Windows Server 2016/2019/2022

### Frameworks
- .NET Core 6.0 SDK
- Windows SDK 10.0.22000.0

### Development Tools
- Visual Studio 2022 (any edition)
- SQL Server Management Studio 18.0

### Database
- SQL Server 2019 (Express/Standard/Enterprise)
- SQL Server LocalDB for development

## Installation

1. **System Requirements Verification**
   - Verify Windows version and updates
   - Check available system resources
   - Validate required permissions

2. **Database Setup**
   ```powershell
   # Run as Administrator
   sqlcmd -S .\SQLEXPRESS -i setup\database\init.sql
   ```

3. **Application Installation**
   ```powershell
   # Install using Windows Installer
   msiexec /i WindowsEventSimulator.msi /qn
   ```

4. **Security Configuration**
   - Configure Windows Authentication
   - Set up role-based access control
   - Enable audit logging

5. **Integration Setup**
   - Configure SIEM export settings
   - Set up monitoring integration
   - Validate connectivity

## Usage

### Quick Start

1. **Generate Single Event**
   ```powershell
   # Using PowerShell module
   New-EventSimulation -TemplateId 4624 -Parameters @{
       LogonType = 2
       TargetUserName = "TestUser"
   }
   ```

2. **Create Event Template**
   ```json
   {
     "templateName": "Failed Login Attempt",
     "eventId": 4625,
     "channel": "Security",
     "parameters": {
       "LogonType": 3,
       "Status": "0xC000006D",
       "SubStatus": "0xC0000064"
     }
   }
   ```

3. **Run Attack Scenario**
   ```powershell
   Start-EventScenario -ScenarioFile "scenarios\privilege-escalation.json"
   ```

### Advanced Usage

- [Template Management Guide](docs/templates/README.md)
- [Scenario Orchestration](docs/scenarios/README.md)
- [SIEM Integration](docs/integration/README.md)
- [Performance Tuning](docs/performance/README.md)

## Development

### Build from Source

```powershell
# Clone repository
git clone https://github.com/windows-event-simulator.git
cd windows-event-simulator

# Build solution
dotnet build -c Release

# Run tests
dotnet test --collect:"XPlat Code Coverage"
```

### Development Guidelines

- Follow [Contribution Guidelines](CONTRIBUTING.md)
- Adhere to [Security Policy](SECURITY.md)
- Review [Code of Conduct](CODE_OF_CONDUCT.md)

## Security

- **Authentication**: Windows Authentication with role-based access
- **Authorization**: Granular permission system for operations
- **Audit**: Comprehensive logging of all operations
- **Input Validation**: Strict validation of all user inputs
- **Rate Limiting**: Configurable limits on event generation
- **Vulnerability Reporting**: Follow [Security Policy](SECURITY.md)

## Support

- [Documentation](docs/)
- [Troubleshooting Guide](docs/support/troubleshooting.md)
- [FAQ](docs/support/faq.md)
- [Issue Tracker](https://github.com/windows-event-simulator/issues)

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Windows Event Log API Documentation
- MITRE ATT&CK Framework
- Security Community Contributors