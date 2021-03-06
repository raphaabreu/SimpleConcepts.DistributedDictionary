﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Sample.Application
{
    public class DistributedWeatherService : IDistributedWeatherService
    {
        private readonly IWeatherService _weatherService;
        private readonly IDistributedCache _distributedCache;

        private readonly DistributedCacheEntryOptions _entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(15)
        };

        public DistributedWeatherService(IWeatherService weatherService, IDistributedCache distributedCache)
        {
            _weatherService = weatherService;
            _distributedCache = distributedCache;
        }

        public async Task<IEnumerable<WeatherForecast>> FetchAsync(CancellationToken cancellationToken)
        {
            var forecasts = new List<WeatherForecast>();

            for (var index = 1; index < 5; index++)
            {
                var date = DateTime.Now.Date.AddDays(index);

                // Get cached daily forecast if it exists and fetch if not.
                var forecast = await _distributedCache
                    .GetOrSetJsonObjectAsync($"single-weather-forecast:{date.ToShortDateString()}",
                        () => _weatherService.FetchForecastAsync(date, cancellationToken),
                        _entryOptions,
                        cancellationToken);

                forecasts.Add(forecast);
            }

            return forecasts.AsEnumerable();
        }
    }
}