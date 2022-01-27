#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["MyHomeLibServer/MyHomeLibServer.csproj", "MyHomeLibServer/"]
COPY ["MyHomeLib.Library/MyHomeLib.Library.csproj", "MyHomeLib.Library/"]
RUN dotnet restore "MyHomeLibServer/MyHomeLibServer.csproj"
COPY MyHomeLibServer MyHomeLibServer/
COPY MyHomeLib.Library MyHomeLib.Library/
WORKDIR "/src/MyHomeLibServer"
RUN dotnet build "MyHomeLibServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MyHomeLibServer.csproj" -c Release -r linux-x64 -p:PublishReadyToRun=true -o /app/publish

FROM base AS final
ENV DOTNET_TieredPGO=1
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyHomeLibServer.dll"]