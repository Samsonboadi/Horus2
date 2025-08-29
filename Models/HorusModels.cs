// Models/HorusModels.cs
using System;
using System.Linq;
using Newtonsoft.Json;

namespace Test.Models
{
    public class HorusConnectionConfig
    {
        public string HorusHost { get; set; } = "10.0.10.100";
        public int HorusPort { get; set; } = 5050;
        public string HorusUsername { get; set; }
        public string HorusPassword { get; set; }
        public string DatabaseHost { get; set; }
        public string DatabasePort { get; set; } = "5432";
        public string DatabaseName { get; set; } = "HorusWebMoviePlayer";
        public string DatabaseUser { get; set; }
        public string DatabasePassword { get; set; }
    }

    public class HorusRecording
    {
        [JsonProperty("Id")]
        public string Id { get; set; }

        [JsonProperty("Endpoint")]
        public string Endpoint { get; set; }

        [JsonProperty("Name")]
        public string Name { get; set; }

        [JsonProperty("Description")]
        public string Description { get; set; }

        [JsonProperty("CreatedDate")]
        public DateTime? CreatedDate { get; set; }

        // Display name for UI binding
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Endpoint?.Split('\\').LastOrDefault() ?? $"Recording {Id}";
    }

    public class HorusImageRequest
    {
        public string RecordingEndpoint { get; set; } = "Rotterdam360\\\\Ladybug5plus";
        public int Count { get; set; } = 5;
        public int Width { get; set; } = 600;
        public int Height { get; set; } = 600;
    }

    public class HorusImage
    {
        [JsonProperty("Index")]
        public int Index { get; set; }

        [JsonProperty("Data")]
        public string Data { get; set; } // Base64 encoded image

        [JsonProperty("Format")]
        public string Format { get; set; }

        [JsonProperty("Timestamp")]
        public string Timestamp { get; set; }

        // Helper method to convert base64 to byte array
        public byte[] GetImageBytes()
        {
            if (string.IsNullOrEmpty(Data))
                return null;

            try
            {
                return Convert.FromBase64String(Data);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}