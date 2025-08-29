using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Test.Services
{
    /// <summary>
    /// Simple utility to test the Python bridge connection without complex service management
    /// </summary>
    public static class BridgeConnectionTester
    {
        /// <summary>
        /// Test if the Python bridge server is responsive
        /// </summary>
        public static async Task<(bool Success, string Message, string Details)> TestBridgeHealthAsync(string bridgeUrl = "http://localhost:5001")
        {
            HttpClient client = null;
            try
            {
                client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var response = await client.GetAsync($"{bridgeUrl}/health");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var healthData = JsonConvert.DeserializeObject<dynamic>(content);
                        var status = healthData?.status?.ToString() ?? "unknown";

                        // Safe boolean conversion
                        var dbConnected = false;
                        var horusConnected = false;

                        if (healthData?.database_connected != null)
                        {
                            if (healthData.database_connected is bool dbBool)
                                dbConnected = dbBool;
                            else if (bool.TryParse(healthData.database_connected.ToString(), out bool dbParsed))
                                dbConnected = dbParsed;
                        }

                        if (healthData?.horus_connected != null)
                        {
                            if (healthData.horus_connected is bool horusBool)
                                horusConnected = horusBool;
                            else if (bool.TryParse(healthData.horus_connected.ToString(), out bool horusParsed))
                                horusConnected = horusParsed;
                        }

                        var details = $"Status: {status}, DB: {(dbConnected ? "Connected" : "Not Connected")}, Horus: {(horusConnected ? "Connected" : "Not Connected")}";

                        return (true, "Bridge server is running", details);
                    }
                    catch (JsonException jsonEx)
                    {
                        return (true, "Bridge server is running", $"Response: {content}");
                    }
                }
                else
                {
                    return (false, $"Bridge server responded with HTTP {response.StatusCode}", content);
                }
            }
            catch (HttpRequestException httpEx)
            {
                return (false, "Cannot reach bridge server", $"Network error: {httpEx.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Bridge server timeout", "Server did not respond within 10 seconds");
            }
            catch (Exception ex)
            {
                return (false, "Bridge test failed", $"Unexpected error: {ex.Message}");
            }
            finally
            {
                try
                {
                    client?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors in test utility
                }
            }
        }

        /// <summary>
        /// Get detailed bridge server status for diagnostics
        /// </summary>
        public static async Task<string> GetDetailedBridgeStatusAsync(string bridgeUrl = "http://localhost:5001")
        {
            try
            {
                var (success, message, details) = await TestBridgeHealthAsync(bridgeUrl);

                if (success)
                {
                    return $"✓ Bridge Status: {message}\n  Details: {details}";
                }
                else
                {
                    return $"✗ Bridge Status: {message}\n  Error: {details}";
                }
            }
            catch (Exception ex)
            {
                return $"✗ Bridge Status Test Failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Test database connection through the bridge
        /// </summary>
        public static async Task<(bool Success, string Message)> TestDatabaseThroughBridgeAsync(
            string host, string port, string database, string user, string password,
            string bridgeUrl = "http://localhost:5001")
        {
            HttpClient client = null;
            try
            {
                client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);

                var testData = new
                {
                    host = host,
                    port = port,
                    database = database,
                    user = user,
                    password = password
                };

                var json = JsonConvert.SerializeObject(testData);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await client.PostAsync($"{bridgeUrl}/test-db", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    var message = result.message?.ToString() ?? "Database test successful";
                    return (true, message);
                }
                else
                {
                    var errorResult = JsonConvert.DeserializeObject<dynamic>(responseContent);
                    var errorMessage = errorResult.error?.ToString() ?? "Database test failed";
                    return (false, errorMessage);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Database test error: {ex.Message}");
            }
            finally
            {
                try
                {
                    client?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors in test utility
                }
            }
        }
    }
}