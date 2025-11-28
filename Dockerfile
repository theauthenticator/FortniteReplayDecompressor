FROM mcr.microsoft.com/dotnet/runtime:9.0-jammy-arm64v8

WORKDIR /app
COPY src/ReplayTest/bin/Release/net9.0/linux-arm64/publish/ .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["./ReplayTest"]