[![Nuget](https://img.shields.io/nuget/v/SimpleConcepts.Extensions.Caching)](https://www.nuget.org/packages/SimpleConcepts.Extensions.Caching/)
[![GitHub license](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](https://raw.githubusercontent.com/raphaabreu/SimpleConcepts.Extensions.Caching/master/LICENSE)

# SimpleConcepts.Extensions.Caching

This package provides several extensions that make working with `IDistributedCache` easier, including Json object serialization, get with fallback, key space partitioning and logging. In addition to these extensions there is also a strongly typed `ISimpleCache<TKey, TValue>` interface that provides a dependency injection friendly and fully customizable wrapper for `IDistributedCache.`

Check the included project in `samples` to see a general purpose implementation.

## Installation

With package Manager:
```
Install-Package SimpleConcepts.Extensions.Caching
```

With .NET CLI:
```
dotnet add package SimpleConcepts.Extensions.Caching
```

An abstractions package is also available at `SimpleConcepts.Extensions.Caching.Abstractions`.

## Extensions

#### Json object serialization

`IDistributedCache` handles only byte arrays. In order to store more usefull values you must first serialize it. There are native convenience extensions for handling strings, but not generic objects.

This package contains extensions that enable `System.Text.Json` serialization for both sync and async methods.

```csharp
// Sync version
_distributedCache.SetJsonObject("test-key-1", new WeatherForecast());
var value1 = _distributedCache.GetJsonObject<WeatherForecast>("test-key-1");

// Async version
await _distributedCache.SetJsonObjectAsync("test-key", new WeatherForecast());
var value2 = await _distributedCache.GetJsonObjectAsync<WeatherForecast>("test-key");
```

There are overloads to pass `DistributedCacheEntryOptions` and `JsonSerializerOptions` as needed.

#### Get with fallback to fetch

A very common scenario is to try to get the value from cache and, if not present, fetch from another service/database. This is usually implemented like this:

```csharp
private readonly DistributedCacheEntryOptions cacheEntryOptions = new DistributedCacheEntryOptions
{
    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
};

public static async Task<Person> GetFromCacheOrFetchAsync(Guid personId)
{
    var key = $"person:{personId}";
    var cachedPerson = await cache.GetJsonObjectAsync(key);
    if (cachedPerson != null)
    {
        return cachedPerson;
    }

    var person = await _personService.FetchAsync(personId);
    await cache.SetJsonObjectAsync(key, person, cacheEntryOptions);

    return person;
}
```

This exemple can be rewritten in a single line:

```csharp
public static Task<Person> GetFromCacheOrFetchAsync(Guid personId)
{
    return cache.GetOrSetJsonObjectAsync($"person:{personId}", 
        () => _personService.FetchAsync(personId), cacheEntryOptions);
}
```

#### Partitioning of `IDistributedCache` key space

This is usefull in scenarions where you have multiple microservices that share a single underlying cache store (like Redis or Sql Server). A great deal of coordination is required to manually ensure that are no key collisions between different microservices.

Example: Lets say that `service-a` wants to cache the name of a person, for that it uses the key `person:123` and stores the string value `"John Doe"` with the expiration of 1 day. A microservice developed by another team, `service-b`, also wants to cache information about persons, and chooses to use the same key pattern `person:123` to store a json serialized object `"{'name':'John Doe'}"` with the expiration of 1 week.

Each time one service reads data that the other has saved, it either won't be able to deserialize (trying to deserialize json) -or- will present the user with jibberish (showing the json string as the person name).

To avoid this, separate cache stores must be configured, which can become expensive depending on the situation. A shared cache store can then be used if you can guarantee that there will be no key collisions.

In order to set a common prefix for all keys used, simply configure the key space on your `Startup` class as follows:

```csharp
// On service-a
services.ConfigureDistributedCacheKeySpace("service-a");

// On service-b
services.ConfigureDistributedCacheKeySpace("service-b");
```

After this, all keys referenced by accessing `IDistributedCache` will be prefixed with the name of the service and will no longer collide: `service-a:person:123` and `service-b:person:123`.

#### Logging `IDistributedCache` operations

To enable automatic logging of operations, you can simply call after configuring your distributed cache:

```csharp
services.AddDistributedCacheLogging();
```

All operations are logged with `Debug` when beginning and with `Information` or `Error` when finished. The completion log message also include the elapsed time in milliseconds.

## SimpleCache

The `ISimpleCache` interface and the corresponding `SimpleCache` is a dependency injection friendly `IDistributedCache` wrapper that exposes simplified, configurable and strongly-typed methods that can have custom serialization and default expiration options for all entries.

The interface comes in two versions: `ISimpleCache<TKey, TValue>` and `ISimpleCache<TValue>`. The first is the most common usage cenario where you want to lookup values by a given key, the second is specific for cases where you have only a single value to be stored.

By default an `ISimpleCache<TKey, TValue>` will:
* Serialize and deserialize `TValue` using `System.Text.Json`.
* Serialize the `TKey` with `key.ToString()`.
* Prefix all keys with `typeof(TValue).FullName + ":"`.
* Have entries that do not expire.

To use it, register the service:

```csharp
// With default options
services.AddSimpleCache<Guid, WeatherForecast>();

// With custom options
services.AddSimpleCache<Guid, WeatherForecast>(opt => opt
    .WithKeyPrefix("weather-forecast")
    // Set custom expiration options
    .WithAbsoluteExpirationRelativeToNow(TimeSpan.FromHours(1))
);

// With value factory as fallback
services.AddSimpleCache<DateTime, WeatherForecast>(opt => opt
    .WithAbsoluteExpirationRelativeToNow(TimeSpan.FromSeconds(15))

    // Configure default value factory to be used when a requested key is not found on cache
    .WithValueFactory((date, provider, token) =>
        provider.GetRequiredService<IWeatherService>().FetchForecastAsync(date, token))
);
```

Then, inject the interface where you need it:

```csharp
public WeatherForecastController(ISimpleCache<DateTime, WeatherForecast> _dailyForecastCache)
{
    __dailyForecastCache = _dailyForecastCache;
}

private async Task<IEnumerable<WeatherForecast>> FetchAllForecastsAsync(CancellationToken cancellationToken)
{
    var forecasts = new List<WeatherForecast>();

    for (var index = 1; index < 5; index++)
    {
        var date = DateTime.Now.Date.AddDays(index);

        // Get cached daily forecast from cache or from default value factory.
        var forecast = await _dailyForecastCache.GetAsync(date, cancellationToken);

        forecasts.Add(forecast);
    }

    return forecasts.AsEnumerable();
}
```
