FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Shared/ Shared/
COPY NotificationService/ NotificationService/
RUN dotnet publish NotificationService/NotificationService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "NotificationService.dll"]
