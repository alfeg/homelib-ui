FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["MyHomeLib.Files/MyHomeLib.Files.csproj", "MyHomeLib.Files/"]
RUN dotnet restore "MyHomeLib.Files/MyHomeLib.Files.csproj"
COPY . .
WORKDIR "/src/MyHomeLib.Files"
RUN dotnet build "MyHomeLib.Files.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyHomeLib.Files.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyHomeLib.Files.dll"]
