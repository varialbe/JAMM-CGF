# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Jamm.Cli/Jamm.Cli.csproj -c Release -r linux-x64 \
    --self-contained true -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false \
    -p:DebugType=None -p:DebugSymbols=false -o /publish

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
COPY --from=build /publish/ /opt/jamm-cgf/
COPY LICENSE /usr/share/doc/jamm-cgf/LICENSE
COPY examples/modkit /usr/share/jamm-cgf/examples/modkit
COPY docs/modding /usr/share/jamm-cgf/modding
RUN ln -s /opt/jamm-cgf/cgfmm /usr/local/bin/cgfmm \
    && mkdir /data && chown app:app /data
ENV HOME=/home/app
USER app
WORKDIR /data
VOLUME ["/data"]
ENTRYPOINT ["cgfmm"]
CMD ["--help"]
