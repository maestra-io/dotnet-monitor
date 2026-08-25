// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Diagnostics.Monitoring.Extension.S3Storage;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Microsoft.Diagnostics.Monitoring.S3StorageTests.UnitTests
{
    public class S3StoragePayloadSigningTests
    {
        private static S3StorageEgressProviderOptions ConstructOptions() => new()
        {
            BucketName = "bucket",
            Endpoint = "https://example.invalid",
            RegionName = "auto",
            AccessKeyId = "accessKeyId",
            SecretAccessKey = "secretAccessKey"
        };

        [Fact]
        public void ItShouldKeepChecksumDefaultsWhenPayloadSigningIsEnabled()
        {
            S3StorageEgressProviderOptions options = ConstructOptions();

            AmazonS3Config configuration = S3Storage.CreateConfiguration(options);

            Assert.Equal(RequestChecksumCalculation.WHEN_SUPPORTED, configuration.RequestChecksumCalculation);
            Assert.Equal(ResponseChecksumValidation.WHEN_SUPPORTED, configuration.ResponseChecksumValidation);
        }

        [Fact]
        public void ItShouldOptOutOfChecksumsWhenPayloadSigningIsDisabled()
        {
            // Checksums travel as an aws-chunked trailer, so leaving them enabled would still emit a
            // STREAMING-...-TRAILER request against an endpoint that cannot accept one.
            S3StorageEgressProviderOptions options = ConstructOptions();
            options.DisablePayloadSigning = true;

            AmazonS3Config configuration = S3Storage.CreateConfiguration(options);

            Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, configuration.RequestChecksumCalculation);
            Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, configuration.ResponseChecksumValidation);
        }

        [Fact]
        public void ItShouldAcceptDisabledPayloadSigningOverHttps()
        {
            S3StorageEgressProviderOptions options = ConstructOptions();
            options.DisablePayloadSigning = true;

            List<ValidationResult> results = new();
            bool valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

            Assert.True(valid);
            Assert.Empty(results);
        }

        [Fact]
        public void ItShouldAcceptDisabledPayloadSigningWithoutAnEndpoint()
        {
            // No endpoint means the AWS-hosted endpoints, which are HTTPS.
            S3StorageEgressProviderOptions options = ConstructOptions();
            options.Endpoint = null;
            options.DisablePayloadSigning = true;

            List<ValidationResult> results = new();
            bool valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

            Assert.True(valid);
            Assert.Empty(results);
        }

        [Theory]
        [InlineData("http://example.invalid")]
        [InlineData("HTTP://example.invalid")]
        public void ItShouldNotAcceptDisabledPayloadSigningOverPlainHttp(string endpoint)
        {
            S3StorageEgressProviderOptions options = ConstructOptions();
            options.Endpoint = endpoint;
            options.DisablePayloadSigning = true;

            List<ValidationResult> results = new();
            bool valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

            Assert.False(valid);
            Assert.Contains(results, r => r.ErrorMessage == Strings.ErrorMessage_EgressS3FailedInsecurePayloadSigningOptOut);
        }

        [Fact]
        public void ItShouldAcceptPlainHttpWhenPayloadSigningIsEnabled()
        {
            S3StorageEgressProviderOptions options = ConstructOptions();
            options.Endpoint = "http://example.invalid";

            List<ValidationResult> results = new();
            bool valid = Validator.TryValidateObject(options, new ValidationContext(options), results, true);

            Assert.True(valid);
            Assert.Empty(results);
        }
    }
}
