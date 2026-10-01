FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/src/Api/PassManager.Api.csproj backend/src/Api/
COPY backend/src/Domain/PassManager.Domain.csproj backend/src/Domain/
COPY backend/src/Application/PassManager.Application.csproj backend/src/Application/
COPY backend/src/Infrastructure/PassManager.Infrastructure.csproj backend/src/Infrastructure/
RUN dotnet restore backend/src/Api/PassManager.Api.csproj

COPY backend/src/ backend/src/
RUN dotnet publish backend/src/Api/PassManager.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "PassManager.Api.dll"]
