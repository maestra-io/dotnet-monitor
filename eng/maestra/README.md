<!-- cspell:words maestra automerged -->
# dotnet-monitor-maestra

The CDP diagnostics sidecar: stock `mcr.microsoft.com/dotnet/monitor:10` with the S3 egress extension rebuilt from upstream `dotnet/dotnet-monitor` `release/10.0` plus the patches in `patches/`. The image is pushed to ECR `dotnet-monitor-maestra` and pinned by digest in DirectCRM `helmfile/fluxcd/cdp-app-tenant/values.yaml`.

This repository's own `src/` is the upstream tree at the fork point and is not built. All maestra changes live here, beside upstream, so upstream flows in without merges:

- `patches/*.patch` are applied onto a fresh upstream checkout at build time (`git apply`). A patch that no longer applies fails the build.
- `Dockerfile` builds the extension, runs the patch's unit tests (`--target test`), and copies the extension into the stock image.
- `.github/workflows/sidecar-image.yml` runs daily, on changes here and on dispatch. It pushes only when the extension bytes or the Dockerfile changed (tag `10-<key>`). The `10` tag then moves and DirectCRM's Renovate bumps the digest pin.
- `renovate.json` pins the MCR `FROM` lines by digest. An MCR servicing rebuild becomes an automerged PR, and its merge rebuilds the image.

To change the extension: apply the patch onto an upstream checkout, edit, and regenerate it with `git diff > eng/maestra/patches/0001-s3-egress-r2.patch` (keep the header).

## Extension options added by the patch

`sessionToken` (string): the session token of temporary (STS-issued) credentials. Requires `accessKeyId` and `secretAccessKey`.

`disablePayloadSigning` (bool): sends `UNSIGNED-PAYLOAD` and drops checksum trailers. Required by S3-compatible services without chunked payloads (Cloudflare R2). The endpoint must be HTTPS.

### Authenticating to S3 using temporary credentials

First Available: maestra-io fork of 10.0 (pending upstream)

Some credential issuers (`sts:AssumeRole`, HashiCorp Vault, and several S3-compatible services) only ever hand out short-lived credentials, which consist of an access key id, a secret access key **and** a session token. All three must be presented on every request; a request signed with only the first two is rejected by the service.

Set `sessionToken` alongside `accessKeyId` and `secretAccessKey` to use such credentials. `endpoint`, `regionName` and `forcePathStyle` are honored as usual, so this works against a custom S3-compatible endpoint.

<details>
  <summary>JSON with a session token</summary>

  ```json
  {
      "Egress": {
          "S3Storage": {
              "monitorS3Blob": {
                  "endpoint": "https://s3.example.com",
                  "bucketName": "myS3Bucket",
                  "accessKeyId": "myTemporaryAccessKeyId",
                  "secretAccessKey": "myTemporarySecretAccessKey",
                  "sessionToken": "mySessionToken",
                  "regionName": "auto",
                  "forcePathStyle": true
              }
          }
      }
  }
  ```
</details>

<details>
  <summary>Kubernetes Secret</summary>

  ```sh
  #!/bin/sh
  kubectl create secret generic my-s3-secrets \
  --from-literal=Egress__S3Storage__monitorS3Blob__bucketName=myS3Bucket \
  --from-literal=Egress__S3Storage__monitorS3Blob__accessKeyId=myTemporaryAccessKeyId \
  --from-literal=Egress__S3Storage__monitorS3Blob__secretAccessKey=myTemporarySecretAccessKey \
  --from-literal=Egress__S3Storage__monitorS3Blob__sessionToken=mySessionToken \
  --from-literal=Egress__S3Storage__monitorS3Blob__regionName=auto \
  --dry-run=client -o yaml | kubectl apply -f -
 ```
</details>

> **Note:** Temporary credentials expire. It is the responsibility of the credential source (for example a sidecar that refreshes a mounted secret) to keep the configured values current; `dotnet monitor` does not renew them.

### Egressing to S3-compatible services without chunked payload support

First Available: maestra-io fork of 10.0 (pending upstream)

By default the AWS SDK uploads with a signed streaming payload (`STREAMING-AWS4-HMAC-SHA256-PAYLOAD`)
and attaches a flexible checksum as an `aws-chunked` trailer. Several S3-compatible services implement
neither, and reject every upload:

```
S3 storage egress failed: STREAMING-AWS4-HMAC-SHA256-PAYLOAD-TRAILER not implemented
```

Because artifacts are streamed as a multi-part upload, this affects all of them — dumps, traces, logs
and GC dumps alike. Cloudflare R2 is the common case.

Set `disablePayloadSigning` to `true` to send `UNSIGNED-PAYLOAD` instead and drop the checksum trailer.
The request is then authenticated by its SigV4-signed headers, and the body is protected by TLS rather
than by the payload signature — so the endpoint must be HTTPS, which is validated at startup.

<details>
  <summary>JSON for an endpoint without chunked payload support</summary>

  ```json
  {
      "Egress": {
          "S3Storage": {
              "monitorS3Blob": {
                  "endpoint": "https://<accountid>.r2.cloudflarestorage.com",
                  "bucketName": "myS3Bucket",
                  "accessKeyId": "myAccessKeyId",
                  "secretAccessKey": "mySecretAccessKey",
                  "regionName": "auto",
                  "disablePayloadSigning": true
              }
          }
      }
  }
  ```
</details>

> **Note:** Leave this off for Amazon S3, which implements both features. Turning it off where it is
> not needed loses the end-to-end integrity check that the payload signature and checksum provide.

### S3 endpoint handling on every authentication path

First Available: maestra-io fork of 10.0 (pending upstream)

> **Behavior change:** `endpoint`, `regionName` and `forcePathStyle` are applied on **every**
> authentication path. Previously they were only applied when `accessKeyId` and `secretAccessKey`
> were both set, and were silently ignored when authenticating via `awsProfileName` or via the
> default credential chain — which made those paths unusable against a custom S3-compatible
> endpoint (the client failed with `No RegionEndpoint or ServiceURL configured`).
>
> If you authenticate with a profile or the default chain, set no `endpoint`, and previously
> relied on the region coming from the profile or `AWS_REGION` while *also* having a stale
> `regionName` configured, the configured `regionName` now wins. Note that an unrecognized
> region name does not fail fast: `RegionEndpoint.GetBySystemName` returns a placeholder region
> and the failure surfaces later, when the request is made.

