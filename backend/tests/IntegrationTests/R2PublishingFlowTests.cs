using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using ComicPlatform.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace ComicPlatform.IntegrationTests;

/// <summary>The API configured exactly like production (PACKAGE_STORE=r2) against an S3-compatible server.</summary>
public sealed class R2ApiFactory(string bucket) : ApiFactory
{
    protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
    {
        ["PACKAGE_STORE"] = "r2",
        ["R2_ENDPOINT"] = Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT"),
        ["R2_ACCESS_KEY"] = Environment.GetEnvironmentVariable("S3_TEST_ACCESS_KEY"),
        ["R2_SECRET_KEY"] = Environment.GetEnvironmentVariable("S3_TEST_SECRET_KEY"),
        ["R2_BUCKET"] = bucket,
        ["PUBLIC_IMAGE_URL"] = "https://img.example.test",
    };
}

public class R2PublishingFlowTests(ITestOutputHelper output)
{
    [S3Fact]
    public async Task Publish_uploads_one_package_to_the_bucket_and_unpublish_withdraws_it()
    {
        var bucket = "cp-flow-" + Guid.NewGuid().ToString("N")[..10];
        using var s3 = new AmazonS3Client(
            new BasicAWSCredentials(Environment.GetEnvironmentVariable("S3_TEST_ACCESS_KEY"), Environment.GetEnvironmentVariable("S3_TEST_SECRET_KEY")),
            new AmazonS3Config { ServiceURL = Environment.GetEnvironmentVariable("S3_TEST_ENDPOINT"), ForcePathStyle = true, AuthenticationRegion = "auto" });
        await s3.PutBucketAsync(bucket);

        var factory = new R2ApiFactory(bucket);
        await ((IAsyncLifetime)factory).InitializeAsync();
        try
        {
            var s = new Scenario(factory);
            var (owner, _) = await s.CreateHouseAsync(await s.AdminAsync(), "Nhóm R2", "chu_r2");
            var comicId = await s.CreateComicAsync(owner, "Truyện Trên R2");
            var chapterId = await s.CreateChapterAsync(owner, comicId, 1);
            await s.UploadAndWaitAsync(owner, chapterId, Scenario.SampleChapterZip(4));

            // Ready, not yet published: nothing may be in the bucket (R2 holds only published packages).
            Assert.Empty((await s3.ListObjectsV2Async(new() { BucketName = bucket })).S3Objects ?? []);

            await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
            var reader = await s.Anonymous().GetAsync($"/api/chapters/{chapterId}/reader");
            var url = reader.GetProperty("package").Str("url");
            Assert.StartsWith("https://img.example.test/comics/", url, StringComparison.Ordinal);
            var key = url["https://img.example.test/".Length..];

            var objects = (await s3.ListObjectsV2Async(new() { BucketName = bucket })).S3Objects;
            var single = Assert.Single(objects);
            Assert.Equal(key, single.Key);
            Assert.Equal(reader.GetProperty("package").GetProperty("size").GetInt64(), single.Size);
            var meta = await s3.GetObjectMetadataAsync(bucket, key);
            Assert.Equal(reader.GetProperty("package").Str("sha256"), meta.Metadata["sha256"]);
            Assert.Equal("public, max-age=31536000, immutable", meta.Headers.CacheControl);
            output.WriteLine($"Published {key} ({single.Size} bytes) to bucket {bucket}");

            // Unpublish → the outbox withdraws the object from the bucket.
            await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/unpublish");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while ((await s3.ListObjectsV2Async(new() { BucketName = bucket })).S3Objects is { Count: > 0 })
            {
                Assert.True(DateTime.UtcNow < deadline, "object was not withdrawn from the bucket");
                await Task.Delay(300);
            }

            // Re-publishing uploads it again from the block volume.
            await owner.PostAsync($"/api/translation-house/chapters/{chapterId}/publish");
            Assert.Single((await s3.ListObjectsV2Async(new() { BucketName = bucket })).S3Objects);
        }
        finally
        {
            await ((IAsyncLifetime)factory).DisposeAsync();
        }
    }
}
