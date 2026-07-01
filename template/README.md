# Product

Created from the Clean Backend rename-only `dotnet new clean-backend` template.

## Run

```bash
dotnet restore src/Product.Api/Product.Api.csproj --locked-mode
dotnet run --project src/Product.Api/Product.Api.csproj
```

## Verify

```bash
dotnet run --project eng/Product.Verify/Product.Verify.csproj -- --product .
```

The Weather endpoint is a deterministic mechanical reference fixture that exercises the endpoint-adapter pattern and Result/Error HTTP mapping. It is not real business logic and does not prove persistence, authorization, tenancy, integrations, or production readiness.

Use `AGENTS.md` and the reference code as the primary guide. The legacy custom generator, when available, is optional convenience for scaffolding; it is not required for architecture compliance.
