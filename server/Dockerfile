FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY Frostbound.Server.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release --no-restore -o /out -m:2
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8443
ENTRYPOINT ["dotnet", "Frostbound.Server.dll"]
