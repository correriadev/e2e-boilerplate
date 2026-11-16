FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY samples/ReferenceApi/ samples/ReferenceApi/
COPY samples/sqlserver/ samples/sqlserver/
RUN dotnet restore samples/ReferenceApi/ReferenceApi.csproj --locked-mode
RUN dotnet publish samples/ReferenceApi/ReferenceApi.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ReferenceApi.dll"]
