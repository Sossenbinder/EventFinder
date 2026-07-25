using System.Net;
using System.Net.Http.Json;
using EventFinder.Api.Endpoints;
using FluentAssertions;

namespace EventFinder.Tests.Api;

public sealed class PlacesEndpointTests(EventFinderApiFactory factory) : IClassFixture<EventFinderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetPlaces_QueryKirchheim_ReturnsKirchheimUnterTeck()
    {
        var response = await _client.GetAsync("/api/places?q=kirchheim");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var places = await response.Content.ReadFromJsonAsync<List<PlaceDto>>();

        places.Should().NotBeNull();
        places!.Should().Contain(p => p.Name == "Kirchheim unter Teck");
    }
}
