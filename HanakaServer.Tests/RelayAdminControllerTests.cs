using HanakaServer.Controllers;
using HanakaServer.Data;
using HanakaServer.Options;
using HanakaServer.Services.Relay;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayAdminControllerTests
{
    [Fact]
    public void Relay_services_validate_and_resolve_with_application_dependencies()
    {
        var services = new ServiceCollection();
        services.AddDbContext<PickleballDbContext>(options =>
            options.UseInMemoryDatabase($"relay-di-{Guid.NewGuid():N}"));
        services.AddSingleton(TimeProvider.System);
        services.Configure<RelayOptions>(_ => { });
        services.AddScoped<RelayAdminService>();
        services.AddScoped<RelayLineupService>();
        services.AddScoped<RelayTeamReader>();
        services.AddScoped<RelayMatchLineupSnapshotService>();
        services.AddScoped<RelayLegacyWriteGuard>();
        services.AddScoped<AdminRelayController>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var lineups = scope.ServiceProvider.GetRequiredService<RelayLineupService>();
        Assert.Same(lineups, scope.ServiceProvider.GetRequiredService<RelayLineupService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AdminRelayController>());
    }

    [Fact]
    public async Task Disabled_admin_preview_does_not_require_schema_or_open_a_database_connection()
    {
        // Invalid SQL host is intentional: none of these actions may access it while disabled.
        await using var db = new PickleballDbContext(new DbContextOptionsBuilder<PickleballDbContext>()
            .UseSqlServer("Server=invalid-relay-test-host;Database=unused;Integrated Security=true;Connect Timeout=1").Options);
        var controller = new AdminRelayController(db, new RelayAdminService(db),
            new RelayLineupService(db, new RelayMatchLineupSnapshotService(db, TimeProvider.System)),
            Microsoft.Extensions.Options.Options.Create(new RelayOptions()));
        Assert.IsType<NotFoundObjectResult>(await controller.Get(1, default));
        Assert.IsType<NotFoundObjectResult>(await controller.SaveSettings(1, new(), default));
        Assert.IsType<NotFoundObjectResult>(await controller.Activate(1, new(), default));
        Assert.IsType<NotFoundObjectResult>(await controller.SaveTeam(1, 10, new(), default));
        Assert.IsType<NotFoundResult>(new RelaySetupController(Microsoft.Extensions.Options.Options.Create(new RelayOptions())).Index(1));
        var options = Microsoft.Extensions.Options.Options.Create(new RelayOptions());
        Assert.IsType<NotFoundResult>(new RelayMatchesController(options).Get(100));
        Assert.IsType<NotFoundResult>(new RefereeRelayMatchesController(options).Get(100));
    }

    [Fact]
    public void Enabled_legacy_match_endpoints_are_retired_without_accessing_the_database()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RelayOptions { AdminPreviewEnabled = true });

        var publicResult = Assert.IsType<ObjectResult>(new RelayMatchesController(options).Get(100));
        Assert.Equal(StatusCodes.Status410Gone, publicResult.StatusCode);

        var refereeController = new RefereeRelayMatchesController(options);
        var refereeResult = Assert.IsType<ObjectResult>(refereeController.Get(100));
        Assert.Equal(StatusCodes.Status410Gone, refereeResult.StatusCode);

        var commandResult = Assert.IsType<ObjectResult>(refereeController.Execute(100));
        Assert.Equal(StatusCodes.Status410Gone, commandResult.StatusCode);
    }

    [Fact]
    public void Legacy_referee_page_redirects_to_the_shared_scoreboard()
    {
        var result = Assert.IsType<LocalRedirectResult>(new RefereePortalController().RelayMatch(100));
        Assert.Equal("/RefereePortal/Matches/100", result.Url);
    }
}
