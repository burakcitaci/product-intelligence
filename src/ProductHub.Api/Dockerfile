FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props ./
COPY src/ProductHub.Api/ProductHub.Api.csproj src/ProductHub.Api/
COPY src/ProductHub.Application/ProductHub.Application.csproj src/ProductHub.Application/
COPY src/ProductHub.Domain/ProductHub.Domain.csproj src/ProductHub.Domain/
COPY src/ProductHub.Infrastructure/ProductHub.Infrastructure.csproj src/ProductHub.Infrastructure/
RUN dotnet restore src/ProductHub.Api/ProductHub.Api.csproj

COPY src/ src/
RUN dotnet publish src/ProductHub.Api/ProductHub.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProductHub.Api.dll"]
