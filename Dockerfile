# Production container for the OpHalo ASP.NET Core API on Railway.
# Railway's Railpack does not build .NET applications, so this explicit image is required.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet publish src/OpHalo.Api/OpHalo.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# .NET's network-authentication support loads the Kerberos GSSAPI library at
# runtime. The ASP.NET image is intentionally slim, so provide that native
# dependency explicitly for PostgreSQL/network authentication paths.
RUN apt-get update \
    && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OpHalo.Api.dll"]
