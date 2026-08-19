# Matches PoolLeagueWeb's Dockerfile shape (official Microsoft .NET Linux
# images, multi-stage build). No test stage yet -- tests/PocketRankingsAccount.Tests
# has no content to run yet; add a test stage here once real tests exist,
# matching League's SB-gate pattern.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/PocketRankingsAccount/PocketRankingsAccount.csproj ./PocketRankingsAccount/
RUN dotnet restore ./PocketRankingsAccount/PocketRankingsAccount.csproj

COPY src/PocketRankingsAccount/ ./PocketRankingsAccount/
RUN dotnet publish ./PocketRankingsAccount/PocketRankingsAccount.csproj -c Release -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /out ./

# No entrypoint script yet -- PoolLeagueWeb's exists to seed a JSON data
# fallback file that Account has no equivalent of. Plain entrypoint until
# there's a real reason for a script.
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "PocketRankingsAccount.dll"]
