// Models/HorusModels.cs
using System;

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
        public string Id { get; set; }
        public string Endpoint { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTime? CreatedDate { get; set; }
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
        public int Index { get; set; }
        public string Data { get; set; } // Base64 encoded image
        public string Format { get; set; }
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
            catch
            {
                return null;
            }
        }
    }
}