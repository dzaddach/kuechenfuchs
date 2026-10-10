FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Kuechenfuchs.Api.csproj ./
RUN dotnet restore Kuechenfuchs.Api.csproj

COPY . ./
RUN dotnet publish Kuechenfuchs.Api.csproj --configuration Release --output /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "Kuechenfuchs.Api.dll"]
