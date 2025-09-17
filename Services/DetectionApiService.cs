using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Test.Models;

namespace Test.Services
{
    public class DetectionApiService
    {
        private readonly HttpClient _httpClient;

        public DetectionApiService(HttpClient httpClient, string baseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        public string BaseUrl { get; set; }

        public async Task<IReadOnlyList<DetectionApiResult>> DetectAsync(DetectionApiRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
                ? throw new InvalidOperationException("Detection API base URL is not configured")
                : BaseUrl.TrimEnd('/');

            var json = JsonConvert.SerializeObject(request);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync($"{baseUrl}/detection/detect", content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Detection API call failed ({response.StatusCode}): {responseBody}");
            }

            var results = JsonConvert.DeserializeObject<List<DetectionApiResult>>(responseBody);
            return results ?? new List<DetectionApiResult>();
        }
    }
}
