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
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OpHalo.Api.dll"]
