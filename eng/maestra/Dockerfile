# CDP diagnostics sidecar: stock mcr.microsoft.com/dotnet/monitor:10 with
# /app/extensions/S3Storage replaced by the S3 egress extension built from
# upstream dotnet/dotnet-monitor release/10.0 plus eng/maestra/patches
# (sessionToken, endpoint honored on every credential path,
# disablePayloadSigning for Cloudflare R2). See eng/maestra/README.md.
#
# Build context: the repo root with upstream checked out into upstream/
# (sidecar-image.yml does that); Dockerfile.dockerignore keeps the context to
# upstream/ and the patches.
#
# The final stage is the stock image, which is shell-less. COPY merge-replaces
# the extension directory; dotnet-monitor loads the extension via
# extension.json -> dotnet-monitor-egress-s3storage.dll, whose deps.json
# governs assembly loading, so any stock file left beneath the copy is inert.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

COPY upstream/ /src
COPY eng/maestra/patches/ /patches/
WORKDIR /src
RUN git apply --verbose /patches/*.patch
# Plain dotnet publish, not the arcade build: global.json pins only
# tools.dotnet (ignored by the CLI), and the darc-int feed is disabled in
# NuGet.config, so an anonymous restore succeeds. ContinuousIntegrationBuild
# and no source revision keep the output byte-identical for identical inputs,
# so /ext.sha256 only moves when the extension really changed.
RUN dotnet publish src/Extensions/S3Storage/S3Storage.csproj -c Release -f net10.0 -o /out \
      -p:ContinuousIntegrationBuild=true -p:IncludeSourceRevisionInInformationalVersion=false \
 && cd /out && find . -type f | LC_ALL=C sort | xargs sha256sum | sha256sum | cut -c1-64 > /ext.sha256

FROM build AS test
RUN dotnet test src/Tests/Microsoft.Diagnostics.Monitoring.S3StorageTests.UnitTests -f net10.0

FROM mcr.microsoft.com/dotnet/monitor:10
# 1654 = the image's APP_UID; keeps ownership sane if a consumer ever drops
# the charts' runAsUser: 0.
COPY --from=build --chown=1654:1654 /out/ /app/extensions/S3Storage/
