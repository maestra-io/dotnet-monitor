# CDP diagnostics-sidecar: stock mcr.microsoft.com/dotnet/monitor:10 with
# /app/extensions/S3Storage replaced by this fork's build of the S3 egress
# extension (sessionToken support, endpoint honored on every credential path,
# disablePayloadSigning for Cloudflare R2 — stock cannot send UNSIGNED-PAYLOAD
# and R2 rejects everything else).
#
# Built by .github/workflows/sidecar-image.yml from the fork's own checkout
# (the workflow's SHA is the provenance; no ARG pinning needed) and pushed to
# ECR dotnet-monitor-maestra. Consumers pin by digest.
#
# The final stage is the stock image, which is shell-less — COPY merge-replaces
# the extension directory; dotnet-monitor loads the extension via
# extension.json -> dotnet-monitor-egress-s3storage.dll, whose deps.json
# governs assembly loading, so any stock file left beneath the copy is inert.
# This fork branch tracks upstream release/10.0, keeping the extension's
# AWSSDK/runtime pins consistent with the base image.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

COPY . /src
WORKDIR /src
# Plain dotnet publish, not the arcade build: global.json pins only
# tools.dotnet (ignored by the CLI), and the darc-int feed is disabled in
# NuGet.config, so an anonymous restore succeeds.
RUN dotnet publish src/Extensions/S3Storage/S3Storage.csproj -c Release -f net10.0 -o /out

FROM mcr.microsoft.com/dotnet/monitor:10
# 1654 = the image's APP_UID; keeps ownership sane if a consumer ever drops
# the charts' runAsUser: 0.
COPY --from=build --chown=1654:1654 /out/ /app/extensions/S3Storage/
