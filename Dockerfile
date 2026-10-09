FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MintChatManager.csproj --configuration Release --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY scripts/run-bot-supervised.sh /app/run-bot-supervised.sh
ENTRYPOINT ["/bin/sh", "/app/run-bot-supervised.sh"]
