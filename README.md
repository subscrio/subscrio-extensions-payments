# Subscrio Payments Extension

TypeScript and .NET implementations of the Subscrio Payments extension.

## Packages

- TypeScript (npm `subscrio-payments`): [typescript/README.md](typescript/README.md)
- .NET (NuGet `Subscrio.Payments`): [dotnet/README.md](dotnet/README.md)

## Local development

This is a standalone repository. Installing the published npm or NuGet packages does not require the other Subscrio repos.

Source builds and tests resolve core through relative paths:

- [subscrio-typescript](https://github.com/subscrio/subscrio-typescript) at `../../core/typescript`
- [subscrio-dotnet](https://github.com/subscrio/subscrio-dotnet) at `../../core/dotnet`

Check out the [hub workspace layout](https://github.com/subscrio/subscrio/blob/main/repos.md) before `npm install` or `dotnet test` in this repo.

## Tests

Requires PostgreSQL. Set `TEST_DATABASE_URL`, or use the defaults described in each package README.

```bash
cd typescript && npm install && npm test
cd dotnet && dotnet test
```

## License

MIT. See [LICENSE](LICENSE).
