# Contributing to PowerPoint Narration Generator

Thank you for your interest in contributing! This document explains the process for contributing to this project.

## Contributor License Agreement

Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit <https://cla.opensource.microsoft.com>.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g. status check, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

## Code of Conduct

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## How to Contribute

### Prerequisites

- [VS Code Dev Container](https://code.visualstudio.com/docs/devcontainers/containers) (recommended — all tooling pre-installed)
- Or manually: .NET 10 SDK, Node 20 LTS, Azure CLI, Docker

### Fork and branch

1. Fork the repository.
2. Create a branch from `main` with a descriptive name:
   - `feat/short-description` for new features
   - `fix/short-description` for bug fixes
   - `docs/short-description` for documentation changes

### Making changes

- Follow the code style conventions defined in [`.editorconfig`](.editorconfig):
  - C# — 4-space indent, Allman braces, `var` when type is apparent
  - TypeScript/JavaScript — 2-space indent, single quotes
- Write or update tests for any changed behaviour.
- Keep commits focused and atomic.

### Running tests locally

**Backend (C#):**

```bash
dotnet test backend-csharp/PptxNarrator.sln
```

**Frontend (end-to-end, requires a running stack):**

```bash
cd frontend
npx playwright install --with-deps
npm run test:e2e
```

### Submitting a pull request

1. Ensure all tests pass locally.
2. Push your branch and open a pull request against `main`.
3. Complete the CLA process if prompted.
4. Describe what the PR does and reference any related issues.
5. A maintainer will review and provide feedback.

## Reporting Issues

Please use [GitHub Issues](../../issues) to report bugs or request features. Before opening a new issue, search to see if one already exists.

For **security vulnerabilities**, please follow the process described in [SECURITY.md](SECURITY.md).
