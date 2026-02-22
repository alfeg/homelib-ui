FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyHomeLib.Library/MyHomeLib.Library.csproj MyHomeLib.Library/
COPY MyHomeLib.Torrent/MyHomeLib.Torrent.csproj  MyHomeLib.Torrent/
COPY MyHomeLib.Web/MyHomeLib.Web.csproj          MyHomeLib.Web/
RUN dotnet restore MyHomeLib.Web/MyHomeLib.Web.csproj

COPY MyHomeLib.Library/ MyHomeLib.Library/
COPY MyHomeLib.Torrent/  MyHomeLib.Torrent/
COPY MyHomeLib.Web/      MyHomeLib.Web/
RUN dotnet publish MyHomeLib.Web/MyHomeLib.Web.csproj -c Release -r linux-x64 --self-contained false -o /app \
	-p:PublishReadyToRun=true \
	-p:PublishReadyToRunComposite=true \
	-p:ReadyToRunUseCrossgen2=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_gcServer=0
ENV DOTNET_GCHeapHardLimitPercent=60
ENV DOTNET_GCConserveMemory=9
EXPOSE 8080

ENTRYPOINT ["dotnet", "MyHomeLib.Web.dll"]
