FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet restore --locked-mode
RUN dotnet publish src/StockPilot.Web -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /data /home/app/.aspnet/DataProtection-Keys && chown -R app:app /data /home/app/.aspnet
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__StockPilot="Data Source=/data/stockpilot.db"
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "StockPilot.Web.dll"]
