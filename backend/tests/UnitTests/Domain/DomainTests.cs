using ComicPlatform.Application.Abstractions;
using ComicPlatform.Application.Identity;
using ComicPlatform.Domain.Catalog;
using ComicPlatform.Domain.Common;
using ComicPlatform.Domain.Tenancy;

namespace ComicPlatform.UnitTests.Domain;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("Tôi Thăng Cấp Một Mình", "toi-thang-cap-mot-minh")]
    [InlineData("Đường đến trường!!", "duong-den-truong")]
    [InlineData("  Ngự Thú Tiến Hoá – Phần 2  ", "ngu-thu-tien-hoa-phan-2")]
    [InlineData("俺だけレベルアップな件", "truyen")]
    [InlineData("", "truyen")]
    public void Slug_strips_vietnamese_diacritics(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.ToSlug(input, 80, "truyen"));

    [Fact]
    public void Search_key_is_diacritic_insensitive()
    {
        var key = TextNormalizer.ToSearchKey("Thám Tử Lừng Danh", "Meitantei Conan", null, "Aoyama   Gōshō");
        Assert.Equal("tham tu lung danh meitantei conan aoyama gosho", key);
        Assert.Contains(TextNormalizer.ToSearchKey("tham tu"), key, StringComparison.Ordinal);
        Assert.Contains(TextNormalizer.ToSearchKey("THÁM TỬ"), key, StringComparison.Ordinal);
    }

    [Fact]
    public void Slug_respects_max_length() =>
        Assert.True(TextNormalizer.ToSlug(new string('a', 500), 80).Length <= 80);
}

public class NaturalSortTests
{
    [Fact]
    public void Orders_digit_runs_numerically()
    {
        var names = new[] { "trang10.jpg", "trang2.jpg", "trang1.jpg", "Trang11.png", "chap/02.jpg", "chap/1.jpg", "trang01.jpg" };
        var sorted = names.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(["chap/1.jpg", "chap/02.jpg", "trang1.jpg", "trang01.jpg", "trang2.jpg", "trang10.jpg", "Trang11.png"], sorted);
    }

    /// <summary>
    /// Writes the server's page order for tricky names to frontend/tests/fixtures/natural-order.json; the
    /// browser's pre-upload preview (zipInspect.ts) must produce exactly the same order.
    /// </summary>
    [Fact]
    public void Shared_order_fixture_for_the_upload_preview_is_up_to_date()
    {
        string[] input =
        [
            "010.jpg", "002.jpg", "3.png", "001.jpg", "1.jpg", "Trang 10.jpg", "trang 9.jpg", "trang-2.webp",
            "chap/02.jpg", "chap/1.jpg", "Chap/3.jpg", "Chương 10/1.jpg", "Chương 2/1.jpg", "a/10.jpg", "a/9.jpg",
            "B.jpg", "a.jpg", "x_001 (2).webp", "x_001.webp", "page_0099.jpg", "page_100.jpg", "ảnh 2.jpg", "anh 3.jpg",
            "20240102_1.jpg", "20240101_2.jpg", "v1.2.png", "v1.10.png",
        ];
        var expected = input.OrderBy(n => n, NaturalStringComparer.Instance).ToArray();

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ".gitignore")))
        {
            dir = dir.Parent;
        }

        var fixtureDir = dir is null ? null : Path.Combine(dir.FullName, "frontend", "tests", "fixtures");
        if (fixtureDir is null || !Directory.Exists(fixtureDir))
        {
            return; // backend checked out alone
        }

        File.WriteAllText(Path.Combine(fixtureDir, "natural-order.json"),
            System.Text.Json.JsonSerializer.Serialize(new { input, expected }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}

public class ChapterStateMachineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Draft_upload_processing_ready_publish_unpublish()
    {
        var chapter = new Chapter(Guid.NewGuid(), 1, null, Now);
        Assert.Equal(ChapterStatus.Draft, chapter.Status);

        Assert.Equal(1, chapter.BeginProcessing(Now));
        Assert.Equal(ChapterStatus.Processing, chapter.Status);
        Assert.Throws<DomainException>(() => chapter.BeginProcessing(Now));

        chapter.CompleteProcessing(Now);
        Assert.Equal(ChapterStatus.Ready, chapter.Status);

        var pkg = Guid.NewGuid();
        chapter.Publish(pkg, 20, Now);
        Assert.Equal(ChapterStatus.Published, chapter.Status);
        Assert.Equal(Now, chapter.PublishedAt);
        Assert.Throws<DomainException>(() => chapter.Publish(pkg, 20, Now));

        chapter.Unpublish(Now);
        Assert.Equal(ChapterStatus.Unpublished, chapter.Status);
        Assert.Null(chapter.PublishedPackageId);
        Assert.Throws<DomainException>(() => chapter.Unpublish(Now));
    }

    [Fact]
    public void Published_chapter_stays_public_while_new_version_processes()
    {
        var chapter = new Chapter(Guid.NewGuid(), 1, null, Now);
        chapter.BeginProcessing(Now);
        chapter.CompleteProcessing(Now);
        chapter.Publish(Guid.NewGuid(), 10, Now);

        Assert.Equal(2, chapter.BeginProcessing(Now));
        Assert.Equal(ChapterStatus.Published, chapter.Status);
        chapter.FailProcessing(Now);
        Assert.Equal(ChapterStatus.Published, chapter.Status);
        Assert.False(chapter.CanDelete);
    }

    [Fact]
    public void Cannot_publish_without_a_built_version()
    {
        var chapter = new Chapter(Guid.NewGuid(), 1, null, Now);
        Assert.Throws<DomainException>(() => chapter.Publish(Guid.NewGuid(), 1, Now));
        chapter.BeginProcessing(Now);
        Assert.Throws<DomainException>(() => chapter.Publish(Guid.NewGuid(), 1, Now));
        chapter.FailProcessing(Now);
        Assert.Equal(ChapterStatus.Failed, chapter.Status);
        Assert.Throws<DomainException>(() => chapter.Publish(Guid.NewGuid(), 1, Now));
        chapter.RetryProcessing(Now);
        Assert.Equal(ChapterStatus.Processing, chapter.Status);
    }
}

public class PermissionTests
{
    [Fact]
    public void Owner_has_everything_staff_only_delegable()
    {
        var owner = new TranslationHouseMember(Guid.NewGuid(), Guid.NewGuid(), HouseRole.Owner, HousePermissions.None, DateTimeOffset.UtcNow);
        Assert.True(owner.Has(HousePermissions.ManageMembers));
        Assert.True(owner.Has(HousePermissions.PublishChapters));

        // Staff can never be granted owner-only permissions, even if requested.
        var staff = new TranslationHouseMember(Guid.NewGuid(), Guid.NewGuid(), HouseRole.Staff, HousePermissions.All, DateTimeOffset.UtcNow);
        Assert.True(staff.Has(HousePermissions.UploadChapters));
        Assert.False(staff.Has(HousePermissions.ManageMembers));
        Assert.False(staff.Has(HousePermissions.ManageProfile));
    }

    [Fact]
    public void Permission_names_round_trip_and_reject_unknown()
    {
        var parsed = PermissionNames.Parse(["uploadChapters", "PUBLISHCHAPTERS", "nuke"], out var unknown);
        Assert.Equal(HousePermissions.UploadChapters | HousePermissions.PublishChapters, parsed);
        Assert.Equal(["nuke"], unknown);
        Assert.Equal(["uploadChapters", "publishChapters"], PermissionNames.ToNames(parsed));
    }
}

public class ObjectKeyTests
{
    [Fact]
    public void Accepts_generated_keys()
    {
        var comic = Guid.NewGuid();
        var chapter = Guid.NewGuid();
        Assert.True(ObjectKeyParser.TryParse(ObjectKeys.Package(comic, chapter, 3), out var parsed));
        Assert.Equal(ObjectKind.Package, parsed.Kind);
        Assert.Equal(3, parsed.Version);
        Assert.True(ObjectKeyParser.TryParse(ObjectKeys.Cover(comic, "0123456789abcdef"), out _));
        Assert.True(ObjectKeyParser.TryParse(ObjectKeys.Logo(comic, "0123456789abcdef"), out _));
        Assert.True(ObjectKeyParser.TryParse(ObjectKeys.HouseCover(comic, "0123456789abcdef"), out var houseCover));
        Assert.Equal(ObjectKind.HouseCover, houseCover.Kind);
        Assert.True(ObjectKeyParser.TryParse(ObjectKeys.Ad("0123456789abcdef"), out var ad));
        Assert.Equal(ObjectKind.Ad, ad.Kind);
        Assert.False(ObjectKeyParser.TryParse("ads/../0123456789abcdef.webp", out _));
    }

    [Theory]
    [InlineData("comics/../../etc/passwd")]
    [InlineData("comics/00000000-0000-0000-0000-000000000000/chapters/00000000-0000-0000-0000-000000000000/v0.pkg")]
    [InlineData("comics/00000000-0000-0000-0000-000000000000/chapters/00000000-0000-0000-0000-000000000000/v1.pkg/../x")]
    [InlineData("COMICS/00000000-0000-0000-0000-000000000000/cover/0123456789abcdef.webp")]
    [InlineData("comics/00000000-0000-0000-0000-000000000000/cover/0123456789ABCDEF.webp")]
    [InlineData("houses/00000000-0000-0000-0000-000000000000/logo/x.webp")]
    [InlineData("")]
    public void Rejects_anything_else(string key) => Assert.False(ObjectKeyParser.TryParse(key, out _));
}
