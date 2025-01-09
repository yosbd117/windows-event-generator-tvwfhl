# Contributing to Windows Event Simulator

Thank you for your interest in contributing to the Windows Event Simulator project! This tool is designed to help security professionals test detection mechanisms and validate security controls through synthetic Windows Event Log entries.

## Table of Contents
- [Introduction](#introduction)
- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Process](#development-process)
- [Pull Request Process](#pull-request-process)
- [Security Considerations](#security-considerations)

## Introduction

We welcome contributions in several areas:
- Event generation engine improvements
- Event template creation and validation
- Security testing enhancements
- Documentation and examples
- Bug fixes and performance optimizations

## Code of Conduct

This project adheres to a Code of Conduct that all contributors are expected to follow. Please read [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) before contributing.

## Getting Started

### Prerequisites

Required development tools:
- Visual Studio 2022 (Enterprise/Professional/Community)
- Git (latest version)
- .NET SDK 6.0
- Windows SDK 10.0.22000.0 or later

### Environment Setup

1. Fork and clone the repository:
```bash
git clone https://github.com/yourusername/windows-event-simulator.git
cd windows-event-simulator
```

2. Configure required Windows permissions:
- Ensure your account has "Generate Security Audits" privilege
- Enable Windows Event Log service
- Configure Event Log access permissions

3. Install dependencies:
```bash
dotnet restore
```

4. Build the project:
```bash
dotnet build
```

5. Run tests:
```bash
dotnet test
```

## Development Process

### Branch Naming Convention

- Features: `feature/event-{description}`
- Bug fixes: `bugfix/event-{description}`
- Security updates: `security/event-{description}`
- Templates: `template/{event-type}`
- Releases: `release/{version}`

### Commit Messages

Format:
```
type(scope): subject

[optional body]
[optional footer]
```

Types:
- feat(event): New event generation feature
- fix(event): Bug fix
- security: Security-related changes
- template: Event template updates
- docs: Documentation changes
- test(event): Test additions/updates
- perf: Performance improvements

### Coding Standards

#### C# Code Style
- Follow Microsoft C# Coding Conventions
- Use Microsoft.CodeAnalysis.CSharp.CodeStyle analyzer
- Implement Microsoft.CodeAnalysis.Security rules

#### XAML Style
- Follow WPF Coding Conventions
- Use Microsoft.CodeAnalysis.WPF.CodeStyle analyzer

### Testing Requirements

#### Unit Tests
- Framework: MSTest
- Minimum coverage: 90%
- Naming convention: EventClass_Method_Scenario
- Must validate event log format compliance

#### Integration Tests
- Test against all supported Windows versions:
  - Windows 10
  - Windows 11
  - Server 2019
  - Server 2022
- Verify event generation in clean environments

## Pull Request Process

### PR Checklist

1. Code Quality:
- [ ] Follows C#/XAML coding standards
- [ ] Passes all analyzer rules
- [ ] Includes comprehensive tests
- [ ] Documentation updated

2. Security:
- [ ] Event log access properly controlled
- [ ] Input parameters sanitized
- [ ] Privilege handling verified
- [ ] Error handling implemented
- [ ] Security impact assessed

3. Performance:
- [ ] Event generation performance tested
- [ ] Resource usage optimized
- [ ] Windows compatibility verified

4. Documentation:
- [ ] Event templates documented
- [ ] MITRE ATT&CK techniques mapped
- [ ] API changes documented
- [ ] Examples provided

### Review Process

1. Submit PR against the appropriate branch
2. Pass automated CI/CD checks
3. Address reviewer feedback
4. Obtain required approvals
5. Pass final security review

## Security Considerations

### Security Guidelines

1. Code Security:
- Validate all event parameters
- Implement proper privilege checks
- Use secure event template processing
- Follow Windows security best practices

2. Testing Security:
- Use isolated test environments
- Sanitize test event data
- Verify privilege separation
- Test security boundaries

### Vulnerability Reporting

For security issues, please follow our [Security Policy](SECURITY.md) and use responsible disclosure practices.

### Additional Resources

- [Bug Report Template](.github/ISSUE_TEMPLATE/bug_report.md)
- Windows Event Log API Documentation
- MITRE ATT&CK Framework
- Microsoft Security Development Lifecycle

## Questions or Need Help?

- Create an issue for bugs or feature requests
- Join our developer discussions
- Review existing documentation

Thank you for contributing to Windows Event Simulator!