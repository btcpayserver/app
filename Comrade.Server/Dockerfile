FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Comrade.Server/Comrade.Server.csproj", "Comrade.Server/"]
RUN dotnet restore "Comrade.Server/Comrade.Server.csproj"
COPY . .
WORKDIR "/src/Comrade.Server"
RUN dotnet build "Comrade.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Comrade.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Comrade.Server.dll"]
