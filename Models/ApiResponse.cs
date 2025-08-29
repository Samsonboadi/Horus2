// Models/ApiResponse.cs
namespace Test.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
        public int StatusCode { get; set; }

        public ApiResponse()
        {
            Success = false;
        }

        public ApiResponse(T data)
        {
            Success = true;
            Data = data;
        }

        public ApiResponse(string error)
        {
            Success = false;
            Error = error;
        }
    }

    // For non-generic responses
    public class ApiResponse : ApiResponse<object>
    {
        public ApiResponse() : base() { }
        public ApiResponse(object data) : base(data) { }
        public ApiResponse(string error) : base(error) { }
    }
}