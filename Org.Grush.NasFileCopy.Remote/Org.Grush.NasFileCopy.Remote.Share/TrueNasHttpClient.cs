using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Remote.Share.Structures;
using Org.Grush.NasFileCopy.Remote.Share.Structures.Enums;

namespace Org.Grush.NasFileCopy.Remote.Share;

public class TrueNasHttpConnectionFailureException(
  string Explanation,
  HttpStatusCode StatusCode
) : Exception($"Connection failure (code={StatusCode}): {Explanation}");

public sealed class TrueNasHttpClient(
  string rawHostname,
  string username,
  string password
) : IAsyncDisposable
{
  private static readonly Regex HostnameRe = new(@"^[a-z0-9_\-\.]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
  private const string ApiVersion = "2.0";
  private readonly Uri Host = new($"https://{rawHostname}/api/v{ApiVersion}/");

  private HttpClient? _client;
  public HttpClient Client
    => _client ?? throw new InvalidOperationException($"Not yet connected; call {nameof(ConnectAsync)}");

  public Uri BuildUri(string relativeUri)
    => new(Host, relativeUri);

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    if (!HostnameRe.IsMatch(rawHostname))
      throw new InvalidOperationException($"Hostname is invalid, should only be a domain or IP address. Got: {rawHostname}");

    var handler = new HttpClientHandler
    {
      ClientCertificateOptions = ClientCertificateOption.Manual,
      ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
      AllowAutoRedirect = false,
    };

    _client = new HttpClient(handler);
    Client.DefaultRequestHeaders.Authorization = new(
      scheme: "Basic",
      parameter: Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"))
    );
    Client.DefaultRequestHeaders.Accept.Add(new("application/json"));

    using var response = await Client.GetAsync(BuildUri("core/ping"), cancellationToken).ConfigureAwait(true);
    if (response.StatusCode is HttpStatusCode.Unauthorized)
      throw new TrueNasHttpConnectionFailureException("User not found or lacks API permissions.", response.StatusCode);

    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true);
    if (body is not "\"pong\"")
      throw new TrueNasHttpConnectionFailureException($"Ping expected result \"pong\" but got '{body}'", response.StatusCode);
  }

  public ImmutableArray<MulticastDelegate> ApiMethods =>
  [
    CallAnyAsync,
    GetUsersAsync,
    GetSystemInfoAsync,
    GetSshSettingsAsync,
    GetDatasetsAsync,
    DeviceGetInfoAsync,
    FsStatAsync,
    FsListdirAsync,
  ];

  public async Task<string> CallAnyAsync(string urlString, CancellationToken cancellationToken)
  {
    var url = BuildUri(urlString);
    if (!url.IsAbsoluteUri || !url.IsWellFormedOriginalString() || !url.AbsoluteUri.StartsWith(Host.AbsoluteUri))
      throw new ArgumentException("Nyeh");

    using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(true);


    if (!response.IsSuccessStatusCode)
    {
      Console.WriteLine("Exited with status {0}", response.StatusCode);
      foreach (var (headerKey, values) in response.Headers)
      {
        Console.WriteLine("\nHeader '{0}':", headerKey);
        foreach (var val in values)
          Console.WriteLine("   {0}", val);
      }
    }

    var prefix = new string('#', 80);
    Console.WriteLine(prefix);
    Console.WriteLine(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(true));
    Console.WriteLine(prefix);
    return "";
  }

  public async Task<ImmutableArray<UserDto>> GetUsersAsync(CancellationToken cancellationToken)
  {
    using var response = await GetWithBodyAsync(BuildUri("user"), new Dictionary<string, object>
    {
      {
        "query-filters",
        new[] {
          new object[]
          {
            "builtin",
            "=",
            false
          }
        }
      }
    }, cancellationToken).ConfigureAwait(true);

    var result = await ReadResponse<ImmutableArray<UserDto>>(response, cancellationToken).ConfigureAwait(true);

    return result;
  }

  public async Task<SystemInfoDto> GetSystemInfoAsync(CancellationToken cancellationToken)
  {
    using var response = await Client.GetAsync(BuildUri("system/info"), cancellationToken);

    return await ReadResponse<SystemInfoDto>(response, cancellationToken);
  }

  public async Task<SshSettingsDto> GetSshSettingsAsync(CancellationToken cancellationToken)
  {
    using var response = await Client.GetAsync(BuildUri("ssh"), cancellationToken);

    return await ReadResponse<SshSettingsDto>(response, cancellationToken);
  }

  public async Task<ImmutableArray<PoolDatasetFilesystemDto>> GetDatasetsAsync(CancellationToken cancellationToken)
  {
    using var response = await Client.GetAsync(BuildUri("pool/dataset"), cancellationToken);

    return await ReadResponse<ImmutableArray<PoolDatasetFilesystemDto>>(response, cancellationToken);
  }

  public async Task<ImmutableDictionary<string, DeviceInfoDto>> DeviceGetInfoAsync(DeviceType type, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync(BuildUri("device/get_info"), value: type.ToString(), cancellationToken);

    return await ReadResponse<ImmutableDictionary<string, DeviceInfoDto>>(response, cancellationToken);
  }

  public async Task<FsStatDto> FsStatAsync(string path, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync(BuildUri("filesystem/stat"), value: path, cancellationToken);

    return await ReadResponse<FsStatDto>(response, cancellationToken);
  }

  public async Task<ImmutableArray<FsListdirDto>> FsListdirAsync(string path, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync(BuildUri("filesystem/listdir"), value: new FsListdirArg(path), cancellationToken);

    return await ReadResponse<ImmutableArray<FsListdirDto>>(response, cancellationToken);
  }

  private record FsListdirArg(string Path);


  private async Task<T> ReadResponse<T>(HttpResponseMessage response, CancellationToken cancellationToken)
  {
    response.EnsureSuccessStatusCode();

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(true);

    return await JsonSerializer.DeserializeAsync<T>(content, options: StandardOptions, cancellationToken).ConfigureAwait(true);
  }

  private async Task<HttpResponseMessage> GetWithBodyAsync<TRequest>(Uri uri, TRequest requestBody, CancellationToken cancellationToken)
  {
    using StringContent content = new(JsonSerializer.Serialize(requestBody));

    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, uri)
    {
      Content = content,
    };

    return await Client.SendAsync(request, cancellationToken).ConfigureAwait(true);
  }

  private static readonly JsonSerializerOptions StandardOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  ValueTask IAsyncDisposable.DisposeAsync()
  {
    Dispose();

    return default;
  }

  private void Dispose()
  {
    Client.Dispose();
  }
}
