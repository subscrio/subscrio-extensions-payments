# Subscrio Payments Extension

TypeScript and .NET implementations of the Subscrio Payments extension.

## Packages

- TypeScript (npm `subscrio-payments`): [typescript/README.md](typescript/README.md)
- .NET (NuGet `Subscrio.Payments`): [dotnet/README.md](dotnet/README.md)

## Local workspace

This repository expects [subscrio-typescript](https://github.com/subscrio/subscrio-typescript) at `../../core/typescript` and, for .NET builds, [subscrio-dotnet](https://github.com/subscrio/subscrio-dotnet) at `../../core/dotnet`. See the [hub workspace layout](https://github.com/subscrio/subscrio/blob/main/repos.md).

## Tests

Requires PostgreSQL (`TEST_DATABASE_URL`).

```bash
cd typescript && npm install && npm test
cd dotnet && dotnet test
```

## License

MIT. See [LICENSE](LICENSE).
