using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace CycleSharingMcp.Tests;

public class OdptFeedSourceTests
{
    private readonly FakeTimeProvider time = new();

    [Fact]
    public async Task FeedSource_ParsesFixture()
    {
        var handler = new FixtureHandler();
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions(null), time);

        var ports = await source.GetPortsAsync(Operator.HelloCycling, default);

        Assert.Equal(3, ports.Count);
        Assert.Equal("https://api-public.odpt.org/api/v4/gbfs/hellocycling/station_information.json", Assert.Single(handler.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task FeedSource_UsesTokenEndpoint()
    {
        var handler = new FixtureHandler();
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions("tok"), time);

        await source.GetStatusesAsync(Operator.Docomo, default);

        Assert.Equal("https://api.odpt.org/api/v4/gbfs/docomo-cycle/station_status.json?acl:consumerKey=tok", Assert.Single(handler.Requests).AbsoluteUri);
    }

    [Fact]
    public async Task FeedSource_StatusCachedForOneMinute_InfoForOneHour()
    {
        var handler = new FixtureHandler();
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions(null), time);

        await source.GetStatusesAsync(Operator.Docomo, default);
        await source.GetPortsAsync(Operator.Docomo, default);
        time.Advance(TimeSpan.FromSeconds(61));
        await source.GetStatusesAsync(Operator.Docomo, default);
        await source.GetPortsAsync(Operator.Docomo, default);

        Assert.Equal(2, handler.Requests.Count(u => u.AbsolutePath.EndsWith("station_status.json")));
        Assert.Equal(1, handler.Requests.Count(u => u.AbsolutePath.EndsWith("station_information.json")));

        time.Advance(TimeSpan.FromMinutes(59));
        await source.GetPortsAsync(Operator.Docomo, default);
        Assert.Equal(2, handler.Requests.Count(u => u.AbsolutePath.EndsWith("station_information.json")));
    }

    [Fact]
    public async Task FeedSource_DocomoVehicleTypes_EmptyWithoutRequest()
    {
        var handler = new FixtureHandler();
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions(null), time);

        Assert.Empty(await source.GetVehicleTypesAsync(Operator.Docomo, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task FeedSource_403_MessageHasNoToken()
    {
        var handler = new FixtureHandler { Status = HttpStatusCode.Forbidden };
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions("secret-token"), time);

        var ex = await Assert.ThrowsAsync<BikeshareException>(() => source.GetStatusesAsync(Operator.Docomo, default));

        Assert.Contains("403", ex.Message);
        Assert.Contains("アクセストークン", ex.Message);
        Assert.DoesNotContain("secret-token", ex.Message);
        Assert.DoesNotContain("odpt.org", ex.Message);
    }

    [Fact]
    public async Task FeedSource_ConnectionFailure_Message()
    {
        var handler = new FixtureHandler { Throw = new HttpRequestException("Connection refused (api.odpt.org:443)") };
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions("secret-token"), time);

        var ex = await Assert.ThrowsAsync<BikeshareException>(() => source.GetPortsAsync(Operator.HelloCycling, default));

        Assert.Contains("接続できません", ex.Message);
        Assert.DoesNotContain("odpt.org", ex.Message);
    }

    [Fact]
    public async Task FeedSource_PassesWaitForOthersToCaches()
    {
        var gate = new TaskCompletionSource();
        var handler = new FixtureHandler { Gate = gate };
        var waits = 0;
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions(null), time, (load, _) =>
        {
            waits++;
            return load;
        });

        var first = source.GetPortsAsync(Operator.Docomo, default);
        var second = source.GetPortsAsync(Operator.Docomo, default);
        gate.SetResult();

        Assert.Equal((await first).Count, (await second).Count);
        Assert.Equal(1, waits);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FeedSource_FailureThenSuccess_Retries()
    {
        var handler = new FixtureHandler { Status = HttpStatusCode.ServiceUnavailable };
        var source = new OdptFeedSource(new HttpClient(handler), new OdptOptions(null), time);

        await Assert.ThrowsAsync<BikeshareException>(() => source.GetStatusesAsync(Operator.Docomo, default));
        handler.Status = HttpStatusCode.OK;
        Assert.Equal(3, (await source.GetStatusesAsync(Operator.Docomo, default)).Count);
    }

    /// <summary>URL の system と feed からフィクスチャを返す。</summary>
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Throw { get; init; }

        /// <summary>設定すると、完了するまで応答を返さない（取得中に別の呼び出しが来る状況を作る）。</summary>
        public TaskCompletionSource? Gate { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            if (Gate is not null) await Gate.Task;
            if (Throw is not null) throw Throw;
            if (Status != HttpStatusCode.OK) return new HttpResponseMessage(Status);

            var segments = request.RequestUri!.AbsolutePath.Split('/');
            var prefix = segments[^2] == "docomo-cycle" ? "docomo" : "hellocycling";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Fixtures.Read($"{prefix}_{segments[^1]}")),
            };
        }
    }
}
