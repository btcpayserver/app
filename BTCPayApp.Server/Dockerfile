FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["BTCPayApp.Server/BTCPayApp.Server.csproj", "BTCPayApp.Server/"]
RUN dotnet restore "BTCPayApp.Server/BTCPayApp.Server.csproj"
COPY . .
WORKDIR "/src/BTCPayApp.Server"
RUN dotnet build "BTCPayApp.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BTCPayApp.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BTCPayApp.Server.dll"]
