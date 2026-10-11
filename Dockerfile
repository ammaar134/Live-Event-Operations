FROM node:24.21.0-bookworm-slim AS web
WORKDIR /web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN npm run build
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /source
COPY global.json ./
COPY src/Api/ src/Api/
RUN dotnet restore src/Api --locked-mode && dotnet publish src/Api -c Release -o /app --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist ./wwwroot
RUN mkdir /app/keys && chown -R app:app /app
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Api.dll"]
