FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/RestService/RestService.csproj src/RestService/
RUN dotnet restore src/RestService/RestService.csproj

COPY src/RestService/ src/RestService/
RUN dotnet publish src/RestService/RestService.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "RestService.dll"]
