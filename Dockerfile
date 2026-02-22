FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyHomeLib.Library/MyHomeLib.Library.csproj MyHomeLib.Library/
COPY MyHomeLib.Torrent/MyHomeLib.Torrent.csproj  MyHomeLib.Torrent/
COPY MyHomeLib.Web/MyHomeLib.Web.csproj          MyHomeLib.Web/
RUN dotnet restore MyHomeLib.Web/MyHomeLib.Web.csproj

COPY MyHomeLib.Library/ MyHomeLib.Library/
COPY MyHomeLib.Torrent/  MyHomeLib.Torrent/
COPY MyHomeLib.Web/      MyHomeLib.Web/
RUN dotnet publish MyHomeLib.Web/MyHomeLib.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyHomeLib.Web.dll"]
