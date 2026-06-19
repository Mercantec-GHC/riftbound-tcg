FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files first for better Docker layer caching during restore.
COPY nuget.config ./
COPY riftbound-tcg.Server/riftbound-tcg.Server.csproj riftbound-tcg.Server/
COPY riftbound-tcg.Engine/riftbound-tcg.Engine.csproj riftbound-tcg.Engine/
COPY riftbound-tcg.Core/riftbound-tcg.Core.csproj riftbound-tcg.Core/
COPY riftbound-tcg.DomainModels/riftbound-tcg.DomainModels.csproj riftbound-tcg.DomainModels/
RUN dotnet restore riftbound-tcg.Server/riftbound-tcg.Server.csproj

# Copy source and publish the app.
COPY . .
RUN dotnet publish riftbound-tcg.Server/riftbound-tcg.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Bind Kestrel to the container HTTP port.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "riftbound-tcg.Server.dll"]
