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
  private readonly Uri Host = new($"https://{rawHostname}/api/{ApiVersion}/");

  private HttpClient? _client;
  public HttpClient Client
    => _client ?? throw new InvalidOperationException($"Not yet connected; call {nameof(ConnectAsync)}");



  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    if (!HostnameRe.IsMatch(rawHostname))
      throw new InvalidOperationException($"Hostname is invalid, should only be a domain or IP address. Got: {rawHostname}");

    _client = new HttpClient();
    Client.BaseAddress = Host;
    Client.DefaultRequestHeaders.Authorization = new(
      scheme: "Basic",
      parameter: Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{username}:{password}"))
    );

    using var response = await Client.GetAsync("core/ping", cancellationToken);
    response.EnsureSuccessStatusCode();
  }

  public ImmutableArray<MulticastDelegate> ApiMethods =>
  [
    GetDatasetsAsync,
    DeviceGetInfoAsync,
    FsStatAsync,
    FsListdirAsync,
  ];

  public async Task<ImmutableList<PoolDatasetFilesystemDto>> GetDatasetsAsync(CancellationToken cancellationToken)
  {
    using var response = await Client.GetAsync("pool/dataset", cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

    var result = await JsonSerializer.DeserializeAsync<ImmutableList<PoolDatasetFilesystemDto>>(content, JsonSerializerOptions.Default, cancellationToken);

    return result!;
  }

  public async Task<ImmutableDictionary<string, DeviceInfoDto>> DeviceGetInfoAsync(DeviceType type, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync("device/get_info", value: type.ToString(), cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

    var result = await JsonSerializer.DeserializeAsync<ImmutableDictionary<string, DeviceInfoDto>>(content, JsonSerializerOptions.Default, cancellationToken);

    return result!;
  }

  public async Task<FsStatDto> FsStatAsync(string path, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync("filesystem/stat", value: path, cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

    var result = await JsonSerializer.DeserializeAsync<FsStatDto>(content, JsonSerializerOptions.Default, cancellationToken);

    return result!;
  }

  public async Task<ImmutableArray<FsListdirDto>> FsListdirAsync(string path, CancellationToken cancellationToken)
  {
    using var response = await Client.PostAsJsonAsync("filesystem/listdir", value: new FsListdirArg(path), cancellationToken);
    response.EnsureSuccessStatusCode();

    await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);

    var result = await JsonSerializer.DeserializeAsync<ImmutableArray<FsListdirDto>>(content, JsonSerializerOptions.Default, cancellationToken);

    return result;
  }

  private record FsListdirArg(string Path);

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
