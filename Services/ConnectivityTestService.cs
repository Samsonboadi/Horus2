// Services/ConnectivityTestService.cs
using System;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Diagnostics;
using Test.Models;
using System.Collections.Generic;

namespace Test.Services
{
    public class ConnectivityTestService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        public ConnectivityTestService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(15);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SphericalImageViewer-ConnectivityTest/1.0");
        }

        /// <summary>
        /// Test basic TCP connectivity to a host and port
        /// </summary>
        public async Task<(bool Success, string Message)> TestTcpConnectivityAsync(string host, int port, int timeoutMs = 10000)
        {
            try
            {
                Debug.WriteLine($"Testing TCP connectivity to {host}:{port}");

                using (var tcpClient = new TcpClient())
                {
                    var connectTask = tcpClient.ConnectAsync(host, port);
                    var timeoutTask = Task.Delay(timeoutMs);

                    var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        var message = $"TCP connection to {host}:{port} timed out after {timeoutMs}ms";
                        Debug.WriteLine($"✗ {message}");
                        return (false, message);
                    }

                    if (connectTask.IsFaulted)
                    {
                        var message = $"TCP connection failed: {connectTask.Exception?.GetBaseException().Message}";
                        Debug.WriteLine($"✗ {message}");
                        return (false, message);
                    }

                    Debug.WriteLine($"✓ TCP connectivity to {host}:{port}: SUCCESS");
                    return (true, "TCP connection successful");
                }
            }
            catch (Exception ex)
            {
                var message = $"TCP connectivity test failed: {ex.Message}";
                Debug.WriteLine($"✗ {message}");
                return (false, message);
            }
        }

        /// <summary>
        /// Test HTTP connectivity to a URL
        /// </summary>
        public async Task<(bool Success, string Message, int? StatusCode)> TestHttpConnectivityAsync(string url)
        {
            try
            {
                Debug.WriteLine($"Testing HTTP connectivity to {url}");

                var response = await _httpClient.GetAsync(url);
                var statusCode = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    var message = $"HTTP connection successful - Status {statusCode}";
                    Debug.WriteLine($"✓ {message}");
                    return (true, message, statusCode);
                }
                else
                {
                    var message = $"HTTP responded with status {statusCode}";
                    Debug.WriteLine($"⚠ {message}");
                    return (false, message, statusCode);
                }
            }
            catch (HttpRequestException httpEx)
            {
                var message = $"HTTP request failed: {httpEx.Message}";
                Debug.WriteLine($"✗ {message}");

                // Check for specific connection refused error
                if (httpEx.Message.Contains("10061") || httpEx.Message.ToLower().Contains("refused"))
                {
                    message += " (Connection refused - server not accepting connections)";
                }

                return (false, message, null);
            }
            catch (TaskCanceledException)
            {
                var message = "HTTP request timed out";
                Debug.WriteLine($"✗ {message}");
                return (false, message, null);
            }
            catch (Exception ex)
            {
                var message = $"HTTP connectivity test failed: {ex.Message}";
                Debug.WriteLine($"✗ {message}");
                return (false, message, null);
            }
        }

        /// <summary>
        /// Comprehensive test of Horus media server connectivity
        /// </summary>
        public async Task<ConnectivityTestResult> TestHorusConnectivityAsync(string horusUrl)
        {
            var result = new ConnectivityTestResult
            {
                TestTimestamp = DateTime.Now,
                TestedUrl = horusUrl
            };

            try
            {
                Debug.WriteLine($"🔍 COMPREHENSIVE HORUS CONNECTIVITY TEST");
                Debug.WriteLine($"Testing URL: {horusUrl}");

                // Parse URL to get host and port
                var uri = new Uri(horusUrl);
                var host = uri.Host;
                var port = uri.Port;

                Debug.WriteLine($"Parsed - Host: {host}, Port: {port}");

                // Step 1: Test TCP connectivity
                Debug.WriteLine("Step 1: Testing TCP connectivity...");
                var (tcpSuccess, tcpMessage) = await TestTcpConnectivityAsync(host, port);
                result.TcpConnectivity = tcpSuccess;
                result.TcpMessage = tcpMessage;

                if (!tcpSuccess)
                {
                    result.OverallSuccess = false;
                    result.PrimaryIssue = "TCP connectivity failed - server unreachable";
                    result.RecommendedActions.Add("Check if Horus media server is running on the target machine");
                    result.RecommendedActions.Add($"Verify network connectivity to {host}");
                    result.RecommendedActions.Add($"Check if port {port} is open and accessible");
                    result.RecommendedActions.Add("Check firewall settings on both client and server");
                    return result;
                }

                // Step 2: Test HTTP connectivity
                Debug.WriteLine("Step 2: Testing HTTP connectivity...");
                var (httpSuccess, httpMessage, statusCode) = await TestHttpConnectivityAsync(horusUrl);
                result.HttpConnectivity = httpSuccess;
                result.HttpMessage = httpMessage;
                result.HttpStatusCode = statusCode;

                if (!httpSuccess && statusCode == null)
                {
                    result.OverallSuccess = false;
                    result.PrimaryIssue = "HTTP service not responding";
                    result.RecommendedActions.Add("Check if Horus HTTP service is running");
                    result.RecommendedActions.Add("Verify Horus server configuration");
                    result.RecommendedActions.Add("Check server logs for HTTP service errors");
                    return result;
                }

                // Step 3: Test specific endpoints
                Debug.WriteLine("Step 3: Testing Horus-specific endpoints...");
                await TestHorusEndpointsAsync(uri.GetLeftPart(UriPartial.Authority), result);

                // Step 4: Overall assessment
                if (result.TcpConnectivity && (result.HttpConnectivity || result.WorkingEndpoints.Count > 0))
                {
                    result.OverallSuccess = true;
                    result.PrimaryIssue = "Connectivity appears to be working";

                    if (!result.HttpConnectivity)
                    {
                        result.RecommendedActions.Add("HTTP main endpoint had issues but other endpoints work");
                        result.RecommendedActions.Add("Consider using alternative endpoint URLs");
                    }
                }
                else
                {
                    result.OverallSuccess = false;
                    result.PrimaryIssue = "Multiple connectivity issues detected";
                    result.RecommendedActions.Add("Address network and service issues identified above");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Connectivity test failed: {ex}");
                result.OverallSuccess = false;
                result.PrimaryIssue = $"Connectivity test failed: {ex.Message}";
                result.RecommendedActions.Add("Check network configuration and server status");
            }

            return result;
        }

        /// <summary>
        /// Test specific Horus endpoints
        /// </summary>
        private async Task TestHorusEndpointsAsync(string baseUrl, ConnectivityTestResult result)
        {
            var testEndpoints = new[]
            {
                "/health",
                "/web/",
                "/web/health",
                "/api/",
                "/"
            };

            foreach (var endpoint in testEndpoints)
            {
                try
                {
                    var testUrl = $"{baseUrl}{endpoint}";
                    var (success, message, statusCode) = await TestHttpConnectivityAsync(testUrl);

                    if (success || (statusCode.HasValue && statusCode.Value < 500))
                    {
                        result.WorkingEndpoints.Add(endpoint);
                        Debug.WriteLine($"✓ Endpoint {endpoint}: Working");
                    }
                    else
                    {
                        Debug.WriteLine($"✗ Endpoint {endpoint}: {message}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"✗ Endpoint {endpoint}: Exception - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Test if the bridge server itself is accessible
        /// </summary>
        public async Task<(bool Success, string Message)> TestBridgeServerAsync(string bridgeUrl = "http://localhost:5001")
        {
            try
            {
                Debug.WriteLine($"Testing bridge server connectivity to {bridgeUrl}");

                var healthUrl = $"{bridgeUrl}/health";
                var (success, message, statusCode) = await TestHttpConnectivityAsync(healthUrl);

                if (success)
                {
                    Debug.WriteLine("✓ Bridge server is accessible");
                    return (true, "Bridge server is running and accessible");
                }
                else
                {
                    Debug.WriteLine($"✗ Bridge server test failed: {message}");
                    return (false, $"Bridge server not accessible: {message}");
                }
            }
            catch (Exception ex)
            {
                var message = $"Bridge server test failed: {ex.Message}";
                Debug.WriteLine($"✗ {message}");
                return (false, message);
            }
        }

        /// <summary>
        /// Run comprehensive diagnostics and return a report
        /// </summary>
        public async Task<string> RunFullDiagnosticsAsync(string horusUrl, string bridgeUrl = "http://localhost:5001")
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("HORUS CONNECTIVITY DIAGNOSTIC REPORT");
            report.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine("=" + new string('=', 60));
            report.AppendLine();

            try
            {
                // Test bridge server
                report.AppendLine("1. BRIDGE SERVER TEST");
                var (bridgeSuccess, bridgeMessage) = await TestBridgeServerAsync(bridgeUrl);
                report.AppendLine($"   Status: {(bridgeSuccess ? "✓ OK" : "✗ FAILED")}");
                report.AppendLine($"   Message: {bridgeMessage}");
                report.AppendLine();

                // Test Horus connectivity
                report.AppendLine("2. HORUS SERVER CONNECTIVITY TEST");
                var horusResult = await TestHorusConnectivityAsync(horusUrl);
                report.AppendLine($"   Overall: {(horusResult.OverallSuccess ? "✓ OK" : "✗ FAILED")}");
                report.AppendLine($"   TCP: {(horusResult.TcpConnectivity ? "✓ OK" : "✗ FAILED")} - {horusResult.TcpMessage}");
                report.AppendLine($"   HTTP: {(horusResult.HttpConnectivity ? "✓ OK" : "✗ FAILED")} - {horusResult.HttpMessage}");

                if (horusResult.WorkingEndpoints.Count > 0)
                {
                    report.AppendLine($"   Working endpoints: {string.Join(", ", horusResult.WorkingEndpoints)}");
                }

                report.AppendLine();

                // Recommendations
                report.AppendLine("3. RECOMMENDATIONS");
                if (horusResult.RecommendedActions.Count > 0)
                {
                    foreach (var action in horusResult.RecommendedActions)
                    {
                        report.AppendLine($"   • {action}");
                    }
                }
                else if (horusResult.OverallSuccess)
                {
                    report.AppendLine("   • Connectivity looks good - retry image operations");
                    report.AppendLine("   • Consider increasing timeout values if issues persist");
                }

                report.AppendLine();

                // Summary
                report.AppendLine("4. SUMMARY");
                if (bridgeSuccess && horusResult.OverallSuccess)
                {
                    report.AppendLine("   🎉 All connectivity tests passed!");
                    report.AppendLine("   Your bridge should be able to retrieve images successfully.");
                }
                else if (!bridgeSuccess)
                {
                    report.AppendLine("   🚨 Bridge server is not accessible");
                    report.AppendLine("   Start the Python bridge server first");
                }
                else if (!horusResult.OverallSuccess)
                {
                    report.AppendLine("   🚨 Horus media server connectivity issues detected");
                    report.AppendLine("   This explains why image retrieval fails with WinError 10061");
                    report.AppendLine("   Fix Horus server accessibility before proceeding");
                }

            }
            catch (Exception ex)
            {
                report.AppendLine($"DIAGNOSTIC ERROR: {ex.Message}");
            }

            return report.ToString();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _httpClient?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing HttpClient: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Result of connectivity testing
    /// </summary>
    public class ConnectivityTestResult
    {
        public DateTime TestTimestamp { get; set; }
        public string TestedUrl { get; set; }
        public bool OverallSuccess { get; set; }
        public string PrimaryIssue { get; set; }

        // TCP Test Results
        public bool TcpConnectivity { get; set; }
        public string TcpMessage { get; set; }

        // HTTP Test Results
        public bool HttpConnectivity { get; set; }
        public string HttpMessage { get; set; }
        public int? HttpStatusCode { get; set; }

        // Endpoint Test Results
        public List<string> WorkingEndpoints { get; set; } = new List<string>();
        public List<string> FailedEndpoints { get; set; } = new List<string>();

        // Recommendations
        public List<string> RecommendedActions { get; set; } = new List<string>();

        public ConnectivityTestResult()
        {
            TestTimestamp = DateTime.Now;
        }
    }
}