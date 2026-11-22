namespace i25Core.Core.DTO
{
    /// <summary>
    /// Paging information returned alongside a page of results.
    /// </summary>
    public class PaginationMeta
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;

        public static PaginationMeta Create(int page, int pageSize, int totalCount)
        {
            return new PaginationMeta
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = pageSize == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize)
            };
        }
    }
}
