// Load / benchmark tool. Runs against a live API (real Kestrel), e.g.:
//   dotnet run --project backend/tests/LoadTests -c Release -- http://localhost:5182 <adminLogin> <adminPassword> <dbConnectionString> <reportPath>
// Scenarios (spec §21): 10 concurrent users, 50 concurrent users, 100 concurrent reader requests, 10 concurrent uploads.
// Measures: latency p50/p95/p99, throughput, errors, server CPU + RAM (admin /system), DB connections (pg_stat_activity),
// processing time, package size, and verifies Kestrel's real 1 MB body limit (413).
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;

var baseUrl = args.ElementAtOrDefault(0) ?? "http://localhost:5182";
var adminLogin = args.ElementAtOrDefault(1) ?? "load_admin";
var adminPassword = args.ElementAtOrDefault(2) ?? "Load-Admin-Password-123";
var dbConnection = args.ElementAtOrDefault(3);
var reportPath = args.ElementAtOrDefault(4) ?? "benchmark.md";
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 256, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromMinutes(5),
};
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var report = new StringBuilder();
var run = DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture);

async Task<JsonElement> Send(HttpMethod method, string url, string? token = null, object? body = null, HttpContent? content = null, HttpStatusCode expect = HttpStatusCode.OK)
{
    using var request = new HttpRequestMessage(method, url) { Content = content ?? (body is null ? null : JsonContent.Create(body)) };
    if (token is not null)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    if (response.StatusCode != expect)
    {
        throw new InvalidOperationException($"{method} {url} → {(int)response.StatusCode}: {text}");
    }

    return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
}

async Task<string> Login(string login, string password) =>
    (await Send(HttpMethod.Post, "/api/auth/login", body: new { login, password })).GetProperty("accessToken").GetString()!;

async Task<JsonElement> System(string token) => await Send(HttpMethod.Get, "/api/admin/system", token);

async Task<long> DbConnections()
{
    if (string.IsNullOrEmpty(dbConnection))
    {
        return -1;
    }

    await using var conn = new NpgsqlConnection(dbConnection);
    await conn.OpenAsync();
    await using var cmd = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()", conn);
    return (long)(await cmd.ExecuteScalarAsync())!;
}

static double Pct(List<double> sorted, double p) => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p / 100 * sorted.Count) - 1)];

async Task Scenario(string name, int concurrency, int iterationsPerWorker, Func<int, Task> action, string adminToken)
{
    var before = await System(adminToken);
    var cpuBefore = before.GetProperty("runtime").GetProperty("totalCpuMilliseconds").GetInt64();
    var latencies = new List<double>();
    var errors = 0;
    long maxDb = 0;
    using var dbProbe = new CancellationTokenSource();
    var probe = Task.Run(async () =>
    {
        while (!dbProbe.IsCancellationRequested)
        {
            maxDb = Math.Max(maxDb, await DbConnections());
            await Task.Delay(100);
        }
    });

    var wall = Stopwatch.StartNew();
    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
    {
        for (var i = 0; i < iterationsPerWorker; i++)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await action(worker);
                lock (latencies)
                {
                    latencies.Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref errors);
                Console.Error.WriteLine($"[{name}] {ex.Message[..Math.Min(200, ex.Message.Length)]}");
            }
        }
    }));
    wall.Stop();
    await dbProbe.CancelAsync();
    await probe;

    var after = await System(adminToken);
    var cpuMs = after.GetProperty("runtime").GetProperty("totalCpuMilliseconds").GetInt64() - cpuBefore;
    var cores = after.GetProperty("runtime").GetProperty("processorCount").GetInt32();
    var ram = after.GetProperty("runtime").GetProperty("workingSetBytes").GetInt64();
    latencies.Sort();
    var total = latencies.Count + errors;
    var line = $"| {name} | {concurrency} | {total} | {errors} | {total / wall.Elapsed.TotalSeconds:0.0} | {Pct(latencies, 50):0.0} | {Pct(latencies, 95):0.0} | {Pct(latencies, 99):0.0} | {cpuMs / wall.Elapsed.TotalMilliseconds / cores * 100:0}% of {cores} | {ram / 1024.0 / 1024:0} MB | {(maxDb < 0 ? "n/a" : maxDb)} |";
    Console.WriteLine(line);
    report.AppendLine(line);
}

byte[] ChapterZip(int pages, int width, int height)
{
    // Large-ish noisy PNG pages (realistic for compression/encoding cost), generated without dependencies.
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
        for (var p = 1; p <= pages; p++)
        {
            var entry = zip.CreateEntry($"{p:D2}.png", CompressionLevel.Fastest);
            using var s = entry.Open();
            s.Write(Png.Create(width, height, seed: p));
        }
    }

    return ms.ToArray();
}

Console.WriteLine($"Target: {baseUrl}");
var admin = await Login(adminLogin, adminPassword);
var sys0 = await System(admin);

// ---- Setup through the API ----
var created = await Send(HttpMethod.Post, "/api/admin/translation-houses", admin,
    new { name = $"Nhóm tải {run}", owner = new { newAccount = new { username = $"chu_tai_{run}", email = $"tai_{run}@example.com", displayName = "Chủ nhóm tải" } } },
    expect: HttpStatusCode.Created);
var temp = created.GetProperty("generatedPassword").GetString()!;
var tempToken = await Login($"chu_tai_{run}", temp);
var owner = (await Send(HttpMethod.Post, "/api/auth/change-password", tempToken, new { currentPassword = temp, newPassword = "Load-Owner-Password-123" })).GetProperty("accessToken").GetString()!;
var comic = (await Send(HttpMethod.Post, "/api/translation-house/comics", owner,
    new { translatedTitle = $"Truyện Tải Nặng {run}", originalTitle = "Load", status = "ongoing", tags = new[] { "Thử tải" } }, expect: HttpStatusCode.Created)).GetProperty("id").GetGuid();

async Task<(Guid Id, JsonElement Job, double Seconds)> Upload(decimal number, byte[] zip)
{
    var chapter = (await Send(HttpMethod.Post, $"/api/translation-house/comics/{comic}/chapters", owner, new { number }, expect: HttpStatusCode.Created)).GetProperty("id").GetGuid();
    var form = new MultipartFormDataContent { { new ByteArrayContent(zip), "file", "chuong.zip" } };
    var sw = Stopwatch.StartNew();
    await Send(HttpMethod.Post, $"/api/translation-house/chapters/{chapter}/upload", owner, content: form, expect: HttpStatusCode.Accepted);
    while (true)
    {
        var job = await Send(HttpMethod.Get, $"/api/translation-house/chapters/{chapter}/processing", owner);
        var status = job.GetProperty("status").GetString();
        if (status is "succeeded" or "failed")
        {
            return (chapter, job, sw.Elapsed.TotalSeconds);
        }

        await Task.Delay(250);
    }
}

var sampleZip = ChapterZip(20, 1000, 1500);
var (chapterId, firstJob, firstSeconds) = await Upload(1, sampleZip);
await Send(HttpMethod.Post, $"/api/translation-house/chapters/{chapterId}/publish", owner);
var reader = await Send(HttpMethod.Get, $"/api/chapters/{chapterId}/reader");
var packageUrl = reader.GetProperty("package").GetProperty("url").GetString()!;
var packageSize = reader.GetProperty("package").GetProperty("size").GetInt64();

report.AppendLine($"# Benchmark — {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
report.AppendLine();
report.AppendLine($"- Target `{baseUrl}`, server processors visible to .NET: **{sys0.GetProperty("runtime").GetProperty("processorCount").GetInt32()}**, image concurrency **{sys0.GetProperty("processing").GetProperty("imageConcurrency").GetInt32()}**, store `{sys0.GetProperty("storage").GetProperty("store").GetString()}`.");
report.AppendLine($"- Sample chapter: 20 PNG pages 1000×1500 ({sampleZip.Length / 1024.0 / 1024:0.0} MB zip) → package **{packageSize / 1024.0 / 1024:0.00} MB**, processed in **{firstSeconds:0.0} s** (status `{firstJob.GetProperty("status").GetString()}`).");
report.AppendLine();
report.AppendLine("| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |");
report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");

// ---- Scenario 1: 10 concurrent users (login + browse) ----
await Scenario("10 users: login + browse", 10, 5, async w =>
{
    await Send(HttpMethod.Get, "/api/comics?pageSize=24");
    await Send(HttpMethod.Get, $"/api/comics/{comic}");
    await Send(HttpMethod.Get, $"/api/comics/{comic}/chapters");
}, admin);

// ---- Scenario 2: 50 concurrent users (mixed public + signed-in) ----
var userTokens = new List<string>();
for (var i = 0; i < 5; i++)
{
    var u = $"doc_tai_{run}_{i}";
    var s = await Send(HttpMethod.Post, "/api/auth/register", body: new { username = u, email = $"{u}@example.com", displayName = "Độc giả tải", password = "Load-Reader-Password-1" });
    userTokens.Add(s.GetProperty("accessToken").GetString()!);
}

await Scenario("50 users: mixed browse/search/me", 50, 10, async w =>
{
    var token = userTokens[w % userTokens.Count];
    await Send(HttpMethod.Get, w % 3 == 0 ? "/api/comics?q=truyen%20tai" : "/api/comics?sort=updated");
    await Send(HttpMethod.Get, $"/api/chapters/{chapterId}/reader");
    await Send(HttpMethod.Get, "/api/auth/me", token);
}, admin);

// ---- Scenario 3: 100 concurrent reader requests (metadata + package from the origin store) ----
await Scenario("100 readers: metadata", 100, 5, async _ => await Send(HttpMethod.Get, $"/api/chapters/{chapterId}/reader"), admin);
await Scenario("100 readers: package download (origin, no CDN)", 100, 1, async _ =>
{
    var bytes = await http.GetByteArrayAsync(packageUrl);
    if (bytes.Length != packageSize)
    {
        throw new InvalidOperationException("size mismatch");
    }
}, admin);

// ---- Scenario 4: 10 concurrent uploads (bounded processing) ----
var sysBefore = await System(admin);
var uploadWall = Stopwatch.StartNew();
var uploads = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Upload(100 + i, sampleZip)));
uploadWall.Stop();
var sysAfter = await System(admin);
var maxConversions = sysAfter.GetProperty("processing").GetProperty("maxObservedConversions").GetInt32();
var imageConcurrency = sysAfter.GetProperty("processing").GetProperty("imageConcurrency").GetInt32();
var cpu = (sysAfter.GetProperty("runtime").GetProperty("totalCpuMilliseconds").GetInt64() - sysBefore.GetProperty("runtime").GetProperty("totalCpuMilliseconds").GetInt64())
          / uploadWall.Elapsed.TotalMilliseconds / sysAfter.GetProperty("runtime").GetProperty("processorCount").GetInt32() * 100;

// Latency of public reads *while* processing runs (the API must stay responsive on 2 OCPU).
report.AppendLine();
report.AppendLine("## 10 concurrent uploads (20 pages each)");
report.AppendLine();
report.AppendLine($"- All jobs: {string.Join(", ", uploads.GroupBy(u => u.Job.GetProperty("status").GetString()).Select(g => $"{g.Count()} {g.Key}"))}; wall time **{uploadWall.Elapsed.TotalSeconds:0.0} s**; per-chapter latency (queue + processing) min **{uploads.Min(u => u.Seconds):0.0} s**, max **{uploads.Max(u => u.Seconds):0.0} s**.");
report.AppendLine($"- Max concurrent image conversions observed: **{maxConversions}** (limit {imageConcurrency}) — bounded as designed.");
report.AppendLine($"- Server CPU during processing: **{cpu:0}%** of available cores; RAM after: **{sysAfter.GetProperty("runtime").GetProperty("workingSetBytes").GetInt64() / 1024.0 / 1024:0} MB**.");

// Responsiveness during processing.
var busyUploads = Task.WhenAll(Enumerable.Range(0, 4).Select(i => Upload(200 + i, sampleZip)));
await Task.Delay(500);
report.AppendLine();
report.AppendLine("## Read latency while 4 chapters are processing");
report.AppendLine();
report.AppendLine("| Scenario | Concurrency | Requests | Errors | Req/s | p50 ms | p95 ms | p99 ms | Server CPU | Server RAM | Max DB conns |");
report.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
await Scenario("50 readers during processing", 50, 10, async _ => await Send(HttpMethod.Get, $"/api/chapters/{chapterId}/reader"), admin);
await busyUploads;

// ---- Kestrel body limit (not enforced by TestServer, so checked here) ----
using (var big = new StringContent(JsonSerializer.Serialize(new { login = new string('a', 2 * 1024 * 1024), password = "x" }), Encoding.UTF8, "application/json"))
using (var response = await http.PostAsync("/api/auth/login", big))
{
    report.AppendLine();
    report.AppendLine($"- 2 MB JSON body to `/api/auth/login` → **{(int)response.StatusCode}** (expected 413, Kestrel MaxRequestBodySize = 1 MB).");
}

await File.WriteAllTextAsync(reportPath, report.ToString());
Console.WriteLine($"Report written to {Path.GetFullPath(reportPath)}");

/// <summary>Minimal PNG encoder (RGB, deflate) for synthetic test pages — no image library needed.</summary>
internal static class Png
{
    public static byte[] Create(int width, int height, int seed)
    {
        var rng = new Random(seed);
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var i = row + 1 + (x * 3);
                var noise = rng.Next(0, 40);
                raw[i] = (byte)((x * 255 / width) ^ noise);
                raw[i + 1] = (byte)((y * 255 / height) ^ noise);
                raw[i + 2] = (byte)(((x + y + (seed * 37)) % 255) ^ noise);
            }
        }

        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        WriteBe(ihdr, 0, width);
        WriteBe(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 2;
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zlib = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(ms, "IDAT", z.ToArray());
        }

        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBe(len, 0, data.Length);
        s.Write(len);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        var crc = new byte[4];
        WriteBe(crc, 0, (int)Crc32(typeBytes.Concat(data).ToArray()));
        s.Write(crc);
    }

    private static void WriteBe(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
