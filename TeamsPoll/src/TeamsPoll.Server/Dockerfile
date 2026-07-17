# Build and run the TeamsPoll bot server.
# Build context is the repository's TeamsPoll/ directory:
#   docker build -f src/TeamsPoll.Server/Dockerfile -t teamspoll .
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 3978
ENV ASPNETCORE_URLS=http://0.0.0.0:3978

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/TeamsPoll.Server/TeamsPoll.Server.csproj src/TeamsPoll.Server/
RUN dotnet restore src/TeamsPoll.Server/TeamsPoll.Server.csproj
COPY . .
RUN dotnet publish src/TeamsPoll.Server/TeamsPoll.Server.csproj -c Release -o /app/out

FROM base AS final
COPY --from=build /app/out .
# Keep the SQLite database on a volume so polls survive container restarts.
VOLUME ["/data"]
ENV ConnectionStrings__Sqlite="Data Source=/data/polls.db"
ENTRYPOINT ["dotnet", "TeamsPoll.Server.dll"]
