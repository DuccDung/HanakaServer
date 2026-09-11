using System.Net.Http.Json;
using HanakaServer.Dtos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HanakaServer.Tests;

public sealed class RelayReserveFormBindingTests
{
    [Fact]
    public async Task Multipart_binding_distinguishes_old_client_omission_explicit_clear_and_new_members()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(ReserveFormBindingProbeController).Assembly);
        await using var app = builder.Build();
        app.MapControllers();
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        foreach (var mode in new[] { "omitted", "clear", "replace" })
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("Đội kiểm thử"), "relayTeamName");
            if (mode != "omitted") form.Add(new StringContent("true"), "relayReserveMembersIncluded");
            if (mode == "replace")
            {
                form.Add(new StringContent("4"), "relayReserveMembers[0].position");
                form.Add(new StringContent("8"), "relayReserveMembers[0].userId");
            }
            using var response = await client.PutAsync("/__test/relay-reserve-form", form);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<UpdateRegistrationPlayersForm>();
            Assert.NotNull(dto);
            Assert.Equal(mode != "omitted", dto.RelayReserveMembersIncluded);
            if (mode == "replace") Assert.Equal(4, Assert.Single(dto.RelayReserveMembers!).Position);
            else Assert.Null(dto.RelayReserveMembers);
        }
    }
}

[ApiController]
[Route("__test/relay-reserve-form")]
public sealed class ReserveFormBindingProbeController : ControllerBase
{
    [HttpPut]
    public IActionResult Put([FromForm] UpdateRegistrationPlayersForm request) => Ok(request);
}
