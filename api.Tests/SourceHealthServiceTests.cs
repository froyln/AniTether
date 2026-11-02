using AniTether.Api.Features.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AniTether.Api.Tests;

public class SourceHealthServiceTests
{
    [Fact]
    public async Task FailedBackgroundRun_ReleasesTheSlot()
    {
        // No DbContext registered, so the run throws as soon as it starts.
        var services = new ServiceCollection().AddHttpClient().BuildServiceProvider();
        var config = new ConfigurationBuilder().Build();
        var service = new SourceHealthService(
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IHttpClientFactory>(),
            new HealthAlertSender(config, NullLogger<HealthAlertSender>.Instance),
            config,
            NullLogger<SourceHealthService>.Instance);

        Assert.True(service.TryStartInBackground());
        for (var i = 0; i < 50 && service.Running; i++)
        {
            await Task.Delay(100);
        }

        Assert.False(service.Running);
        Assert.True(service.TryStartInBackground());
    }
}
