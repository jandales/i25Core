using i25Core.Core.DTO;

namespace i25Core.BusinessLayer.Parameters
{
    public class GetResponse
    {
        public bool Success { get; set; }
        public string? Message { get; set; }

        /// <summary>Optional translation key for the message (for example "messages.retrieved_many"). When set, the controller shows its translation in the request's language.</summary>
        public string? MessageKey { get; set; }
        public object? Data { get; set; }

        /// <summary>Set only when the request asked for a page.</summary>
        public PaginationMeta? Pagination { get; set; }
    }
}
