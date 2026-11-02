# Contributing

Thanks for your interest in AniTether. Contributions are welcome.

## Ground rules

- **No bundled sources.** AniTether deliberately ships with an empty `sources` table and no
  site-specific scraping code. Please don't add source rows, site URLs or download/torrent
  extractors for third-party streaming sites in a PR.
- Keep changes small and focused; one concern per PR.
- Don't commit secrets (`.env`, `secrets/` are git-ignored).

## Before you start

Read [docs/development.md](docs/development.md) for how to run the project and find your way
around.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Make your change, following the conventions in the code around it.
3. Run the full check and make sure it passes:

   ```bash
   dotnet build && dotnet test && npm --prefix web run build
   ```

4. Open a pull request with a clear description of what and why.

## License

By contributing you agree your work is licensed under the [GPLv3](LICENSE).
