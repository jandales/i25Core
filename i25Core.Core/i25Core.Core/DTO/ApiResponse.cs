namespace i25Core.Core.DTO
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public T? Data { get; set; }
        public IDictionary<string, string[]>? Errors { get; set; }
        public string? TraceId { get; set; }
        public PaginationMeta? Pagination { get; set; }

        public static ApiResponse<T> SuccessResult(
            T? data,
            string? message = null,
            PaginationMeta? pagination = null)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Message = message,
                Data = data,
                Errors = null,
                Pagination = pagination
            };
        }

        public static ApiResponse<T> Failure(
            string message,
            IDictionary<string, string[]>? errors = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Message = message,
                Data = default,
                Errors = errors
            };
        }
    }
}