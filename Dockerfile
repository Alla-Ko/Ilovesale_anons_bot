# syntax=docker/dockerfile:1
# Збірка та публікація ASP.NET Core 8 (Razor Pages).
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Announcement.csproj", "./"]
RUN dotnet restore "Announcement.csproj"

COPY . .
RUN dotnet publish "Announcement.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
# Порт задає Render через PORT у runtime; EXPOSE лише для документації.
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Announcement.dll"]
