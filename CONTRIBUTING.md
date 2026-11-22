# Contributing to File Search

Thanks for taking the time to help. Bug reports, ideas and pull requests are all welcome.

## Reporting a bug or asking for a feature

- Search the [existing issues](https://github.com/vladano/win-file-search/issues) first.
- Use the issue forms ([new issue](https://github.com/vladano/win-file-search/issues/new/choose)). For bugs, include the app version (exe Properties > Details), your Windows version, the search you ran, and `%AppData%\FileSearch\logs\filesearch.log` if it has entries.
- Security problems: please do not open a public issue. See [SECURITY.md](SECURITY.md).

## Building and testing

You need Windows 10 or 11 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```powershell
git clone https://github.com/vladano/win-file-search.git
cd win-file-search
dotnet build FileSearch.sln -c Release
dotnet test FileSearch.sln
```

The build treats warnings as errors and enforces the code style in `.editorconfig`, so a clean build means the style is fine too.

## Pull requests

1. Fork the repository and create a branch from `main` (for example `fix/pdf-encoding` or `feature/context-lines`).
2. Keep each pull request to one change. Small pull requests get reviewed faster.
3. Add or update tests for behaviour you change. Engine code goes in `tests/FileSearch.Core.Tests`, view models and services in `tests/FileSearch.App.Tests`.
4. Make sure `dotnet build` and `dotnet test` pass locally. CI runs the same steps on every pull request.
5. Update `README.md` if you change something a user can see (options, command line, files on disk).
6. Fill in the pull request template.

The README section **Adding things** lists the places to touch when adding a search option, a mode, a dialog or a file format change.

## Code style

- C# conventions from `.editorconfig`; the build checks them.
- The engine (`FileSearch.Core`) must stay free of UI code.
- View models talk to the user through `IDialogService`, never through windows directly.
- Prefer clear names and small methods over comments that explain what the code does. Comment the *why*.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE) of this project.

Please follow the [Code of Conduct](CODE_OF_CONDUCT.md).
