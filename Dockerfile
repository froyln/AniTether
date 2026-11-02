# One image: the React SPA built into the API's wwwroot, served from the same origin.

FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY Directory.Build.props ./
COPY api/AniTether.Api.csproj api/
RUN dotnet restore api/AniTether.Api.csproj
COPY api/ api/
RUN dotnet publish api/AniTether.Api.csproj -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /out ./
COPY --from=web /src/web/dist ./wwwroot
# Data Protection key ring lives on a volume mounted here; created now so the non-root
# user owns it (a fresh named volume copies this ownership).
RUN mkdir /keys && chown app:app /keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/keys
EXPOSE 8080
ENTRYPOINT ["dotnet", "AniTether.Api.dll"]
