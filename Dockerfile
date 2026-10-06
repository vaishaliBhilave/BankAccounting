# 1) React UI
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

# 2) API (+ the UI build copied into wwwroot so one container serves everything)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/ src/
COPY db/ db/
COPY --from=web /web/dist src/Host/wwwroot
RUN dotnet restore src/Host/BankAccounting.Host.csproj
RUN dotnet publish src/Host/BankAccounting.Host.csproj -c Release -o /app --no-restore

# 3) Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Trust X-Forwarded-* from the platform's TLS-terminating proxy (needed on most free PaaS hosts).
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "BankAccounting.Host.dll"]
