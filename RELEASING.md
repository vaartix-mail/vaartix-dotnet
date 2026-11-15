# Releasing

Published to NuGet as `Vaartix` by GitHub Actions with NuGet trusted publishing: no long-lived API key.

## One-time setup

1. Create a nuget.org account (as Vaartix) with 2FA.
2. nuget.org → your profile → **Trusted Publishing → Add policy**: GitHub, owner `vaartix-mail`,
   repository `vaartix-dotnet`, workflow `publish.yml`, environment `nuget`.
3. GitHub → Settings → Environments → `nuget`: required reviewer, deployment tags `v*` only, and an
   environment variable `NUGET_USER` set to your nuget.org user name (not a secret).

## Each release

1. Update `<Version>` in `src/Vaartix/Vaartix.csproj` and `Version` in `VaartixClient.cs`, and move the
   "Unreleased" notes in `CHANGELOG.md`. Merge to `main`.
2. Create a GitHub release with the tag `v<version>` on `main`, then approve the publish run.
