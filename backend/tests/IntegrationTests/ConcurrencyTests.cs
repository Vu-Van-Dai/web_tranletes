using System.Diagnostics;
using System.Net;
using ComicPlatform.Infrastructure.Processing;
using ComicPlatform.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ComicPlatform.IntegrationTests;

public class ConcurrencyTests(ApiFactory factory, ITestOutputHelper output) : IClassFixture<ApiFactory>
{
    private readonly Scenario _s = new(factory);

    [Fact]
    public async Task Concurrent_readers_and_uploads_stay_correct_and_bounded()
    {
        var admin = await _s.AdminAsync();
        var (owner, _) = await _s.CreateHouseAsync(admin, "Nhóm Đồng Thời", "chu_dong_thoi");
        var (comicId, chapterId) = await _s.PublishedChapterAsync(owner, "Truyện Đông Người Đọc");

        // 50 concurrent users browsing + 100 concurrent reader-metadata requests.
        var sw = Stopwatch.StartNew();
        var browse = Enumerable.Range(0, 50).Select(async i =>
        {
            using var r = await _s.Anonymous().SendAsync(HttpMethod.Get, i % 2 == 0 ? "/api/comics" : $"/api/comics/{comicId}");
            return r.StatusCode;
        });
        var readers = Enumerable.Range(0, 100).Select(async _ =>
        {
            using var r = await _s.Anonymous().SendAsync(HttpMethod.Get, $"/api/chapters/{chapterId}/reader");
            return r.StatusCode;
        });
        var statuses = await Task.WhenAll(browse.Concat(readers));
        output.WriteLine($"150 concurrent public requests in {sw.ElapsedMilliseconds} ms");
        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.OK, s));

        // 10 concurrent uploads: all accepted, processed with bounded image parallelism.
        var metrics = factory.Services.GetRequiredService<ProcessingMetrics>();
        metrics.ResetMaxObserved();
        var chapters = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            chapters.Add(await _s.CreateChapterAsync(owner, comicId, 100 + i));
        }

        var zip = Scenario.SampleChapterZip(6);
        sw.Restart();
        var uploads = await Task.WhenAll(chapters.Select(async id =>
        {
            using var r = await owner.UploadAsync($"/api/translation-house/chapters/{id}/upload", zip, "chuong.zip");
            return r.StatusCode;
        }));
        Assert.All(uploads, s => Assert.Equal(HttpStatusCode.Accepted, s));

        foreach (var id in chapters)
        {
            var job = await Scenario.WaitForJobAsync(owner, id, timeoutSeconds: 180);
            Assert.Equal("succeeded", job.Str("status"));
        }

        output.WriteLine($"10 chapters × 6 pages processed in {sw.ElapsedMilliseconds} ms; max concurrent conversions = {metrics.MaxObservedConversions}");
        Assert.InRange(metrics.MaxObservedConversions, 1, 2);

        // Racing publishes of the same chapter: exactly one wins.
        var racer = chapters[0];
        var publishes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            using var r = await owner.SendAsync(HttpMethod.Post, $"/api/translation-house/chapters/{racer}/publish", new { });
            return r.StatusCode;
        }));
        Assert.Equal(1, publishes.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(1, publishes.Count(s => s == HttpStatusCode.Conflict));
    }
}
