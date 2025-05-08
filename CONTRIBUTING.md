# Contributing to SenseNet Index Maintenance Suite

Thank you for your interest in contributing to the SenseNet Index Maintenance Suite! This document provides guidelines and instructions for contributing.

## Code of Conduct

By participating in this project, you are expected to uphold our Code of Conduct:

- Use welcoming and inclusive language
- Be respectful of differing viewpoints and experiences
- Gracefully accept constructive criticism
- Focus on what is best for the community
- Show empathy towards other community members

## How Can I Contribute?

### Reporting Bugs

When reporting bugs, please include:

- A clear and descriptive title
- Steps to reproduce the issue
- Expected vs. actual behavior
- Screenshots if applicable
- Your environment details (OS, .NET version, etc.)

### Suggesting Enhancements

When suggesting enhancements:

- Use a clear and descriptive title
- Provide a step-by-step description of the suggested enhancement
- Explain why this enhancement would be useful to most users
- List some other applications where this enhancement exists, if applicable

### Pull Requests

- Fill in the required template
- Do not include issue numbers in the PR title
- Include screenshots and animated GIFs in your pull request whenever possible
- Follow the C# coding style
- Include appropriate tests
- Document new code based on the Documentation Styleguide
- End all files with a newline

## Development Process

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add some amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## Styleguides

### Git Commit Messages

- Use the present tense ("Add feature" not "Added feature")
- Use the imperative mood ("Move cursor to..." not "Moves cursor to...")
- Limit the first line to 72 characters or less
- Reference issues and pull requests liberally after the first line

### C# Styleguide

- Follow Microsoft's C# coding conventions
- Use 4 spaces for indentation
- Use PascalCase for class names and method names
- Use camelCase for method arguments and local variables
- Use underscores for private fields: `_privateField`
- Interfaces should start with "I": `IIndexManager`
- Use XML documentation comments for public API

## License

By contributing, you agree that your contributions will be licensed under the project's MIT License.