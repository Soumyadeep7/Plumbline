# Builds Plumbline as one container: the Fable frontend is compiled and copied into the
# .NET API's wwwroot, so a single web service serves both the UI and /api.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Node.js is needed to bundle the frontend with Vite.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# Lets the Fable tool run on the .NET 8 runtime.
ENV DOTNET_ROLL_FORWARD=Major

WORKDIR /src/frontend
COPY frontend/ ./
RUN dotnet tool restore && npm install && npm run build

WORKDIR /src/backend
COPY backend/ ./
RUN dotnet publish Plumbline.Api.csproj -c Release -o /app \
    && mkdir -p /app/wwwroot \
    && cp -r /src/frontend/dist/. /app/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
# The host supplies PORT; 10000 is the default when it does not.
ENV PORT=10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "Plumbline.Api.dll"]
