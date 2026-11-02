# AniTether web app

The React + TypeScript + Vite front end of AniTether. It talks to the API over JSON; in
development `npm run dev` serves it on http://localhost:5173 and forwards `/api` to the API
on port 5000.

```bash
npm ci            # install
npm run dev       # dev server with hot reload
npm run build     # type-check + production build (part of the project's verification)
npm run lint      # oxlint
```

Setup, code layout and conventions: [development guide](../docs/development.md).
