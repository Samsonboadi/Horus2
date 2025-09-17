using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Test.Models
{
    public class DetectionApiRequest
    {
        [JsonProperty("image_data")]
        public string ImageData { get; set; }

        [JsonProperty("image_url")]
        public string ImageUrl { get; set; }

        [JsonProperty("horus_current_image")]
        public string HorusCurrentImage { get; set; }

        [JsonProperty("detection_text")]
        public string DetectionText { get; set; } = "pole";

        [JsonProperty("model")]
        public string Model { get; set; } = "langsam";

        [JsonProperty("yaw")]
        public double Yaw { get; set; }

        [JsonProperty("pitch")]
        public double Pitch { get; set; }

        [JsonProperty("roll")]
        public double Roll { get; set; }

        [JsonProperty("fov")]
        public double Fov { get; set; }

        [JsonProperty("confidence_threshold")]
        public double ConfidenceThreshold { get; set; } = 0.22;

        [JsonProperty("iou_threshold")]
        public double IoUThreshold { get; set; } = 0.5;
    }

    public class DetectionApiBoundingBox
    {
        [JsonProperty("x1")]
        public double X1 { get; set; }

        [JsonProperty("y1")]
        public double Y1 { get; set; }

        [JsonProperty("x2")]
        public double X2 { get; set; }

        [JsonProperty("y2")]
        public double Y2 { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }
    }

    public class DetectionApiGroundPoint
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }
    }

    public class DetectionApiGeographicLocation
    {
        [JsonProperty("lat")]
        public double Latitude { get; set; }

        [JsonProperty("lon")]
        public double Longitude { get; set; }

        [JsonProperty("alt")]
        public double Altitude { get; set; }
    }

    public class DetectionApiResult
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("bbox")]
        public DetectionApiBoundingBox BoundingBox { get; set; }

        [JsonProperty("mask")]
        public string Mask { get; set; }

        [JsonProperty("ground_point")]
        public DetectionApiGroundPoint GroundPoint { get; set; }

        [JsonProperty("geographic_location")]
        public DetectionApiGeographicLocation GeographicLocation { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> AdditionalProperties { get; set; }
    }
}
