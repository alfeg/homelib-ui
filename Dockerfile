FROM node:22-bookworm-slim AS ui-build
WORKDIR /src/MyHomeLib.Ui
COPY MyHomeLib.Ui/package*.json ./
RUN npm ci
COPY MyHomeLib.Ui/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyHomeLib.Library/MyHomeLib.Library.csproj MyHomeLib.Library/
COPY MyHomeLib.Torrent/MyHomeLib.Torrent.csproj  MyHomeLib.Torrent/
COPY MyHomeLib.Web/MyHomeLib.Web.csproj          MyHomeLib.Web/
RUN dotnet restore MyHomeLib.Web/MyHomeLib.Web.csproj

COPY MyHomeLib.Library/ MyHomeLib.Library/
COPY MyHomeLib.Torrent/  MyHomeLib.Torrent/
COPY MyHomeLib.Web/      MyHomeLib.Web/
COPY --from=ui-build /src/MyHomeLib.Ui/dist/ MyHomeLib.Web/wwwroot/
RUN dotnet publish MyHomeLib.Web/MyHomeLib.Web.csproj -c Release --self-contained false -o /app \
	-p:BuildClientApp=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_gcServer=0
ENV DOTNET_GCHeapHardLimitPercent=35
ENV DOTNET_GCConserveMemory=9
ENV DOTNET_GCRetainVM=0
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyHomeLib.Web.dll"]
