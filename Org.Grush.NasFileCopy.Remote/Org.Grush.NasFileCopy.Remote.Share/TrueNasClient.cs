using System.Collections.Immutable;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Org.Grush.NasFileCopy.Remote.Share.Structures;

namespace Org.Grush.NasFileCopy.Remote.Share;

public sealed class TrueNasClient(
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

    var handler = new HttpClientHandler()
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
    // Client.DefaultRequestHeaders.Accept.Add(new("application/json"));

    using var response = await Client.GetAsync(BuildUri("core/ping"), cancellationToken);
    response.EnsureSuccessStatusCode();
    var body = await response.Content.ReadAsStringAsync(cancellationToken);
    if (body is not "\"pong\"")
      throw new InvalidOperationException("Ping failed");
  }

  public ImmutableArray<MulticastDelegate> ApiMethods =>
  [
    GetDatasetsAsync,
    DeviceGetInfoAsync,
    FsStatAsync,
    FsListdirAsync,
  ];

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

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

    return await JsonSerializer.DeserializeAsync<T>(content, options: new()
    {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    }, cancellationToken);
  }

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
