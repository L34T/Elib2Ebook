using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Configs;
using Core.Exceptions;
using Core.Extensions;
using Core.Misc;
using Core.Types.AuthorToday;
using Core.Types.Book;
using Core.Types.Common;
using Microsoft.Extensions.Logging;

namespace Core.Logic.Getters;

public class AuthorTodayGetter(BookGetterConfig config) : GetterBase(config)
{
    private const string AT_CERT = AppSecrets.AuthorTodayCert;

    private bool _bypass;
    private bool _singleFetchUsed;
    protected override Uri SystemUrl => new("https://author.today/");

    private Uri _apiUrl => new("https://api.author.today/");

    /// <summary>
    ///     IP сайта author.today
    /// </summary>
    private Uri _systemIp => new("https://185.26.98.195/");

    /// <summary>
    ///     IP сайта api.author.today
    /// </summary>
    private Uri _apiIp => new("https://185.26.98.195/");

    private Uri ApiUrl => _bypass ? _apiIp : _apiUrl;

    private Uri SiteUrl => _bypass ? _systemIp : SystemUrl;

    private string UserId { get; set; } = string.Empty;

    private string hashedCert => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(AT_CERT))).ToUpperInvariant();

    protected override string GetId(Uri url)
    {
        return url.GetSegment(2);
    }

    public override async Task Init()
    {
        Config.Client.DefaultRequestHeaders.Add("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        Config.Client.DefaultRequestHeaders.Add("Accept-Language", "ru");
        Config.Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
        Config.Client.DefaultRequestHeaders.Add("User-Agent", "okhttp/4.12.0 X_AT_API");
        Config.Client.DefaultRequestHeaders.Add("X-AT-Client", "android_1.8.013-GMS");
        Config.Client.DefaultRequestHeaders.Add("X-AT-Certificate", hashedCert);

        var response = await Config.Client.GetAsync(_apiUrl);
        _bypass = response.StatusCode != HttpStatusCode.OK;
        Config.Logger.LogInformation(_bypass
            ? $"Сайт {_apiUrl} не доступен. Работаю через {_apiIp}"
            : $"Сайт {_apiUrl} доступен. Работаю через него");

        Config.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "guest");
    }

    public override async Task Authorize()
    {
        if (!Config.HasCredentials) return;

        const string directory = "ATCache";
        if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

        var cachePath = $"{directory}/{Config.Options.Login.RemoveInvalidChars()}";

        if (await TryAuthorizeFromCacheAsync(cachePath)) return;

        await AuthorizeByPasswordAsync(cachePath);
    }

    private async Task<string> GetUserIdAsync()
    {
        try
        {
            var response = await Config.Client.SendWithTriesAsync(() =>
                GetDefaultMessage(ApiUrl.MakeRelativeUri("/v1/account/current-user"), _apiUrl));

            if (response.StatusCode != HttpStatusCode.OK) return null;

            var user = await response.Content.ReadFromJsonAsync<AuthorTodayUser>();
            return user?.Id.ToString();
        }
        catch (Exception ex)
        {
            Config.Logger.LogDebug(ex, "AuthorToday: не удалось получить идентификатор пользователя.");
            return null;
        }
    }

    private async Task<bool> TryAuthorizeFromCacheAsync(string cachePath)
    {
        if (!File.Exists(cachePath)) return false;

        try
        {
            if (DateTime.Now - File.GetLastWriteTime(cachePath) >= TimeSpan.FromHours(24))
            {
                File.Delete(cachePath);
                return false;
            }

            var json = await File.ReadAllTextAsync(cachePath);
            var savedAuth = json.Deserialize<AuthorTodayAuthResponse>();
            if (string.IsNullOrWhiteSpace(savedAuth?.Token)) return false;

            Config.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", savedAuth.Token);

            UserId = await GetUserIdAsync();
            if (!string.IsNullOrWhiteSpace(UserId))
            {
                Config.Logger.LogInformation("AuthorToday: успешно авторизовались с помощью кэша");
                return true;
            }
        }
        catch (Exception ex)
        {
            Config.Logger.LogDebug(ex, "AuthorToday: не удалось авторизоваться через кэш.");
            try { File.Delete(cachePath); } catch { /* ignore */ }
        }

        Config.Client.DefaultRequestHeaders.Authorization = null;
        return false;
    }

    private async Task AuthorizeByPasswordAsync(string cachePath)
    {
        var response = await Config.Client.SendAsync(GetDefaultMessage(
            ApiUrl.MakeRelativeUri("/v1/account/login-by-password"), _apiUrl,
            JsonContent.Create(new { Config.Options.Login, Config.Options.Password })));

        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadFromJsonAsync<AuthorTodayAuthResponse>();
            throw new Elib2EbookAuthException($"Не удалось авторизоваться. {err?.Message}");
        }

        var data = await response.Content.ReadFromJsonAsync<AuthorTodayAuthResponse>();
        if (string.IsNullOrWhiteSpace(data?.Token))
            throw new Elib2EbookAuthException("Сайт не вернул token после успешного запроса авторизации.");

        Config.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", data.Token);

        UserId = await GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(UserId))
            throw new Elib2EbookAuthException("Не удалось получить идентификатор пользователя после авторизации.");

        Config.Logger.LogInformation("Успешно авторизовались");

        try
        {
            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            Config.Logger.LogDebug(ex, "AuthorToday: не удалось записать кэш авторизации.");
        }
    }

    public override async Task<Book> Get(Uri url)
    {
        url = SystemUrl.MakeRelativeUri($"/work/{GetId(url)}");
        var details = await GetBookDetails(GetId(url));

        var book = new Book(url)
        {
            Cover = await GetCover(details),
            Chapters = await FillChapters(details),
            Title = details.Title,
            Author = GetAuthor(details),
            CoAuthors = GetCoAuthors(details),
            Annotation = details.Annotation,
            Seria = GetSeria(details)
        };

        await FillAdditional(book, details);

        return book;
    }

    private async Task FillAdditional(Book book, AuthorTodayBookDetails details)
    {
        if (!Config.Options.HasAdditionalType(AdditionalTypeEnum.Images) || details.GalleryImages == null ||
            details.GalleryImages.Length == 0) return;

        Config.Logger.LogInformation("Загружаю дополнительные иллюстрации");
        foreach (var image in details.GalleryImages)
            book.AdditionalFiles.Add(AdditionalTypeEnum.Images, await SaveImage(SystemUrl.MakeRelativeUri(image.Url)));
        Config.Logger.LogInformation("Дополнительные иллюстрации загружены");
    }

    private HttpRequestMessage GetDefaultMessage(Uri uri, Uri host, HttpContent content = null)
    {
        var message = new HttpRequestMessage(content == null ? HttpMethod.Get : HttpMethod.Post, uri);
        message.Content = content;
        message.Version = Config.Client.DefaultRequestVersion;

        foreach (var header in Config.Client.DefaultRequestHeaders) message.Headers.Add(header.Key, header.Value);

        message.Headers.Host = host.Host;

        return message;
    }

    private async Task<AuthorTodayBookDetails> GetBookDetails(string bookId)
    {
        var response = await Config.Client.SendWithTriesAsync(() =>
            GetDefaultMessage(ApiUrl.MakeRelativeUri($"/v1/work/{bookId}/details"), _apiUrl));
        if (response.StatusCode != HttpStatusCode.OK) throw new Elib2EbookParseException("Книга не найдена");

        return await response.Content.ReadFromJsonAsync<AuthorTodayBookDetails>();
    }

    private Author GetAuthor(AuthorTodayBookDetails book)
    {
        return new Author(book.AuthorFio, SystemUrl.MakeRelativeUri($"/u/{book.AuthorUserName}/works"));
    }

    private IEnumerable<Author> GetCoAuthors(AuthorTodayBookDetails book)
    {
        var result = new List<Author>();
        if (!string.IsNullOrWhiteSpace(book.CoAuthorFio))
            result.Add(new Author(book.CoAuthorFio, SystemUrl.MakeRelativeUri($"/u/{book.CoAuthorUserName}/works")));

        return result;
    }

    private Seria GetSeria(AuthorTodayBookDetails book)
    {
        if (!book.SeriesId.HasValue) return null;

        return new Seria
        {
            Name = book.SeriesTitle.Trim(),
            Number = book.SeriesWorkNumber.HasValue ? book.SeriesWorkNumber.ToString() : string.Empty,
            Url = SystemUrl.MakeRelativeUri($"/work/series/{book.SeriesId}")
        };
    }

    private Task<TempFile> GetCover(AuthorTodayBookDetails book)
    {
        return !string.IsNullOrWhiteSpace(book.CoverUrl)
            ? SaveImage(book.CoverUrl.AsUri())
            : Task.FromResult<TempFile>(null);
    }

    protected override HttpRequestMessage GetImageRequestMessage(Uri uri)
    {
        if (uri.IsSameHost(SystemUrl) || uri.IsSameSubDomain(SystemUrl))
            return GetDefaultMessage(SiteUrl.MakeRelativeUri(uri.AbsolutePath), uri);

        return base.GetImageRequestMessage(uri);
    }

    private async Task<IEnumerable<Chapter>> FillChapters(AuthorTodayBookDetails book)
    {
        var result = new List<Chapter>();
        if (Config.Options.NoChapters) return result;

        foreach (var atChapter in await GetChapters(book))
        {
            var title = atChapter.Title.ReplaceNewLine();
            var prefix = _singleFetchUsed ? "sf " : string.Empty;
            Config.Logger.LogInformation($"{prefix}Загружаю главу {title.CoverQuotes()}");

            var chapter = new Chapter
            {
                Title = title
            };

            if (atChapter.IsSuccessful)
            {
                var chapterDoc = atChapter.Decode(UserId, AT_CERT).AsHtmlDoc();
                chapter.Images = await GetImages(chapterDoc, SystemUrl);
                chapter.Content = chapterDoc.DocumentNode.InnerHtml;
            }

            result.Add(chapter);
        }

        return result;
    }

    private async Task<IEnumerable<AuthorTodayChapter>> GetChapters(AuthorTodayBookDetails book)
    {
        var result = new List<AuthorTodayChapter>();

        var fetchMode = Config.GetSetting("AT:FetchMode", "Batch");
        if (fetchMode.Equals("Single", StringComparison.OrdinalIgnoreCase))
        {
            _singleFetchUsed = true;
            foreach (var chapter in book.Chapters.Where(c => !c.IsDraft && c.isAvailable).OrderBy(c => c.SortOrder))
            {
                var uri = ApiUrl.MakeRelativeUri($"/v1/work/{book.Id}/chapter/{chapter.Id}/text");
                Config.Logger.LogInformation($"Загружаю главу {chapter.Title.CoverQuotes()}");
                var response = await Config.Client.SendWithTriesAsync(() => GetDefaultMessage(uri, _apiUrl));
                var rchapter = await response.Content.ReadFromJsonAsync<AuthorTodayChapter>();
                if (rchapter != null && rchapter.Text != "")
                {
                    Config.Logger.LogInformation($"Загружена глава {rchapter.Title.CoverQuotes()}");
                    chapter.Text = rchapter.Text;
                    chapter.Key = rchapter.Key;
                    chapter.IsSuccessful = true;
                    result.Add(chapter);
                }
            }
        }
        else
        {
            foreach (var chunk in book.Chapters.Where(c => !c.IsDraft).OrderBy(c => c.SortOrder).Chunk(100))
            {
                var ids = string.Join("&", chunk.Select((c, i) => $"ids[{i}]={c.Id}"));
                var uri = ApiUrl.MakeRelativeUri($"/v1/work/{book.Id}/chapter/many-texts?{ids}");
                var response = await Config.Client.SendWithTriesAsync(() => GetDefaultMessage(uri, _apiUrl));
                var chapters = await response.Content.ReadFromJsonAsync<AuthorTodayChapter[]>();
                if (chapters != null) result.AddRange(chapters.Where(c => c.Code != "NotFound"));
            }
        }

        return SliceToc(result, c => c.Title);
    }
}
