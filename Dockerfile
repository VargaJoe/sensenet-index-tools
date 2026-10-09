# Digest pins and locked NuGet restore make the build inputs repeatable.
FROM mcr.microsoft.com/dotnet/sdk:8.0.425-bookworm-slim@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80 AS build
WORKDIR /src
COPY . .
RUN dotnet restore sensenet-index-tools.sln --locked-mode --source https://api.nuget.org/v3/index.json
RUN dotnet publish src/MainProgram/sn-index-maintenance-suite.csproj -c Release --no-restore -o /out/cli \
    && dotnet publish src/WebApp/WebApp/WebApp.csproj -c Release --no-restore -o /out/web

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim@sha256:a3cd573ac05cf88ca496e3309cb7f7c44aa16664352d1ee41eda93154f50cdbf AS runtime
WORKDIR /app/web
COPY --from=build /out/cli /app/cli
COPY --from=build /out/web /app/web
COPY --chmod=755 docker/entrypoint.sh /app/entrypoint.sh
RUN mkdir -p /state/data /state/reports /state/keys /state/copies /state/backups \
    && chown -R 1654:1654 /state && chmod -R 700 /state
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_ENVIRONMENT=Production \
    HOME=/tmp \
    INDEXTOOLS_CONTAINER_RUNTIME=1
USER 1654:1654
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD ["dotnet", "/app/cli/sn-index-maintenance-suite.dll", "healthcheck"]
ENTRYPOINT ["/app/entrypoint.sh"]
CMD ["web"]
