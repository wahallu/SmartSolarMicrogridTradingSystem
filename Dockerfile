FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["backend/SmartSolar.Api/SmartSolar.Api.csproj", "backend/SmartSolar.Api/"]
RUN dotnet restore "backend/SmartSolar.Api/SmartSolar.Api.csproj"

COPY . .
WORKDIR /src/backend/SmartSolar.Api
RUN dotnet publish "SmartSolar.Api.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartSolar.Api.dll"]
