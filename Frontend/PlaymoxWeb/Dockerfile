FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["PlaymoxWeb.csproj", "."]
RUN dotnet restore "./PlaymoxWeb.csproj"

COPY . .
RUN dotnet publish "./PlaymoxWeb.csproj" -c Release -o /app/publish

FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot .
COPY docker-entrypoint.d/40-playmox-runtime-config.sh /docker-entrypoint.d/40-playmox-runtime-config.sh
RUN sed -i 's/\r$//' /docker-entrypoint.d/40-playmox-runtime-config.sh
RUN chmod +x /docker-entrypoint.d/40-playmox-runtime-config.sh
