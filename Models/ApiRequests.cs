// Models/ApiRequests.cs
using System.Collections.Generic;

namespace Test.Models
{
    public class LoadImagesRequest
    {
        public string Directory { get; set; }
        public string ServerUrl { get; set; }
        public string FilePattern { get; set; } = "*.jpg;*.png;*.jpeg";
        public bool IncludeSubdirectories { get; set; } = true;
        public int MaxImages { get; set; } = 1000;
    }

    public class RenderRequest
    {
        public string ImagePath { get; set; }
        public double Yaw { get; set; }
        public double Pitch { get; set; }
        public double Roll { get; set; }
        public double Fov { get; set; }
        public int Width { get; set; } = 800;
        public int Height { get; set; } = 600;
        public string ProjectionType { get; set; } = "equirectangular";
    }

    public class DetectionRequest : RenderRequest
    {
        public string Model { get; set; }
        public string DetectionText { get; set; }
        public double ConfidenceThreshold { get; set; } = 0.3;
        public double IoUThreshold { get; set; } = 0.5;
    }

    public class ServerInfo
    {
        public string Version { get; set; }
        public string Status { get; set; }
    }
}