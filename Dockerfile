FROM --platform=$BUILDPLATFORM node:lts-alpine AS ui-build

WORKDIR /src/inpx-parser
COPY inpx-parser/package*.json ./
RUN npm ci
COPY inpx-parser/ ./
RUN npm run build

WORKDIR /src/MyHomeLib.Ui
COPY MyHomeLib.Ui/package*.json ./
RUN npm ci
COPY MyHomeLib.Ui/ ./
RUN npm run build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# .git is not in the build context, so the commit shown in the /m footer is passed in:
#   docker compose build --build-arg GIT_SHA=$(git rev-parse HEAD)
ARG GIT_SHA=

COPY MyHomeLib.Library/MyHomeLib.Library.csproj MyHomeLib.Library/
COPY MyHomeLib.Web/MyHomeLib.Web.csproj          MyHomeLib.Web/
RUN dotnet restore MyHomeLib.Web/MyHomeLib.Web.csproj

COPY MyHomeLib.Library/ MyHomeLib.Library/
COPY MyHomeLib.Web/      MyHomeLib.Web/

# Genre label JSON lives with the SPA and is linked into the Web project by its csproj.
COPY MyHomeLib.Ui/src/assets/ MyHomeLib.Ui/src/assets/

RUN dotnet build MyHomeLib.Web/MyHomeLib.Web.csproj -c Release --no-restore -p:BuildClientApp=false \
	-p:SourceRevisionId=$GIT_SHA

COPY --from=ui-build /src/MyHomeLib.Ui/dist/ MyHomeLib.Web/wwwroot/
RUN dotnet publish MyHomeLib.Web/MyHomeLib.Web.csproj -c Release --self-contained false -o /app \
	-p:BuildClientApp=false -p:SourceRevisionId=$GIT_SHA

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
