using Cloudweather.Report.Config;
using Cloudweather.Report.DataAccess;
using Cloudweather.Report.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Cloudweather.Report.BusinessLogic
{
    /// <summary>
    /// Agregates data from multiple external sources to build a weather report
    /// </summary>
    public interface IWeatherReportAggregator
    {
        /// <summary>
        /// Build and returns a Weather Report.
        /// Persists WeeklyWeatherReport data
        /// </summary>
        /// <param name="zip"></param>
        /// <param name="days"></param>
        /// <returns></returns>
        public Task<WeatherReport> BuildReport(string zip, int days);
    }
    public class WeatherReportAggregator : IWeatherReportAggregator
    {
        private readonly IHttpClientFactory _http;
        private readonly ILogger<WeatherReportAggregator> _logger;
        private readonly WeatherDataConfig _weatherDataConfig;
        private readonly WeatherReportDbContext _db;

        public WeatherReportAggregator(
            IHttpClientFactory http,
            ILogger<WeatherReportAggregator> logger,
            IOptions<WeatherDataConfig> weatherConfig,
            WeatherReportDbContext db
    )
        {
            _http = http;
            _logger = logger;
            _weatherDataConfig = weatherConfig.Value;
            _db = db;
        }

        public async Task<WeatherReport> BuildReport(string zip, int days)
        {
            var httpClient = _http.CreateClient();

            var precipiData = await FetchPrecipitationData(httpClient, zip, days);
            var totalSnow = GetTotalSnow(precipiData);
            var totalRain = GetTotalRain(precipiData);
            _logger.LogInformation(
                $"zip: {zip} over last {days} days: " +
                $"total snow: {totalSnow}, rain: {totalRain}"
            );


            var tempData = await FetchTemperatureData(httpClient, zip, days);
            var averageHightTemp = tempData.Average(t => t.TempHighF);
            var averageLowTemp = tempData.Average(t => t.TempLowF);
            _logger.LogInformation(
                $"zip: {zip} over last {days} days: " +
                $"lo temp: {averageLowTemp}, hi temp: {averageHightTemp}"
            );
         
            var weatherReport = new WeatherReport
            {
                AverageHighF = Math.Round(averageHightTemp, 1),
                AverageLowF = Math.Round(averageLowTemp, 1),
                RainfallTotalInches = totalRain,
                SnowTotalInches = totalSnow,
                ZipCode = zip,
                CreatedOn = DateTime.UtcNow,

            };

            // ToDo: Use 'cached' weather reports instead of making round trips when possible?
            _db.Add(weatherReport);
            await _db.SaveChangesAsync();

            return weatherReport;
        }

        private static decimal GetTotalSnow(IEnumerable<PrecipitationModel> precipData)
        {
            var totalSnow = precipData
                .Where(p => p.WeatherType == "snow")
                .Sum(p => p.AmountInches);
            return Math.Round(totalSnow, 1);
        }

        private static decimal GetTotalRain(IEnumerable<PrecipitationModel> precipData)
        {
            var totalRain = precipData
                .Where(p => p.WeatherType == "rain")
                .Sum(p => p.AmountInches);
            return Math.Round(totalRain, 1);
        }

        private async Task<List<TemperatureModel>> FetchTemperatureData(HttpClient httpClient, string zip, int days)
        {
            var endpoint = BuildTemperatureEndpoint(zip, days);
            var temperatureRecords = await httpClient.GetAsync(endpoint);
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var temperatureData = await temperatureRecords
                .Content
                .ReadFromJsonAsync<List<TemperatureModel>>(jsonSerializerOptions);
            return temperatureData ?? new List<TemperatureModel>();
        }

        private async Task<List<PrecipitationModel>> FetchPrecipitationData(HttpClient httpClient, string zip, int days)
        {
            var endpoint = BuildPrecipitationEndpoint(zip, days);
            var precipRecords = await httpClient.GetAsync(endpoint);
            var T = precipRecords.Content;
            var jsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            var precipData = await precipRecords
                .Content
                .ReadFromJsonAsync<List<PrecipitationModel>>(jsonSerializerOptions);
            return precipData ?? new List<PrecipitationModel>();
        }

        private string BuildTemperatureEndpoint(string zip, int days)
        {
            var temperatureServiceProtocol = _weatherDataConfig.TemperatureDataProtocol;
            var temperatureServiceHost = _weatherDataConfig.TemperatureDataHost;
            var temperatureServicePort = _weatherDataConfig.TemperatureDataPort;
            return $"{temperatureServiceProtocol}://{temperatureServiceHost}:{temperatureServicePort}/observation/{zip}?days={days}";
        }

        private string BuildPrecipitationEndpoint(string zip, int days)
        {
            var precipServiceProtocol = _weatherDataConfig.PrecipDataProtocol;
            var precipServiceHost = _weatherDataConfig.PrecipDataHost;
            var precipServicePort = _weatherDataConfig.PrecipDataPort;
            return $"{precipServiceProtocol}://{precipServiceHost}:{precipServicePort}/observation/{zip}?days={days}";
        }
    }
}
