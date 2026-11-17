using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Trazo.Api.Support;
using Xunit;

public sealed class R8MappingStoreTests
{
    [Fact]
    public async Task StoreAsync_WritesMappingForExactVersion_AndReturnsSha256()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trazo-r8-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = CreateStore(directory);
            var bytes = Encoding.UTF8.GetBytes("# compiler: R8\n# pg_map_id: test");
            await using var input = new MemoryStream(bytes);

            var hash = await store.StoreAsync("0.2.2+30029", input, CancellationToken.None);

            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hash);
            Assert.Equal(Encoding.UTF8.GetString(bytes),
                await File.ReadAllTextAsync(Path.Combine(directory, "0.2.2+30029.mapping.txt")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryRetraceAsync_DoesNotRunWithoutVersionOrMatchingMapping()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trazo-r8-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = CreateStore(directory, "/path/that/does/not/exist/retrace");

            Assert.Null(await store.TryRetraceAsync(null, "a.b: error", CancellationToken.None));
            Assert.Null(await store.TryRetraceAsync("0.2.2+30029", "a.b: error", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TryRetraceAsync_KeepsFallbackWhenRetraceExecutableIsUnavailable()
    {
        var directory = Path.Combine(Path.GetTempPath(), "trazo-r8-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = CreateStore(directory, "/path/that/does/not/exist/retrace");
            await using var input = new MemoryStream(Encoding.UTF8.GetBytes("# mapping"));
            await store.StoreAsync("0.2.2+30029", input, CancellationToken.None);

            Assert.Null(await store.TryRetraceAsync("0.2.2+30029", "a.b: error", CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UploadKeyComparison_RejectsMissingOrIncorrectKeys()
    {
        Assert.True(R8MappingStore.IsValidUploadKey("a-long-random-secret", "a-long-random-secret"));
        Assert.False(R8MappingStore.IsValidUploadKey("a-long-random-secret", "wrong"));
        Assert.False(R8MappingStore.IsValidUploadKey(null, "a-long-random-secret"));
        Assert.False(R8MappingStore.IsValidUploadKey("a-long-random-secret", null));
    }

    private static R8MappingStore CreateStore(string directory, string executable = "retrace")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Diagnostics:MappingsPath"] = directory,
            ["Diagnostics:RetraceExecutable"] = executable
        }).Build();
        return new R8MappingStore(configuration, NullLogger<R8MappingStore>.Instance);
    }
}
