using i25Core.BusinessLayer.Parameters;

namespace i25Core.BusinessLayer
{
    public class GetRequest
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        public List<GetFilter> Filters { get; set; } = new();
        public List<GetSort> Sorts { get; set; } = new();

        /// <summary>1-based page number. Paging is applied only when Page or PageSize is set.</summary>
        public int? Page { get; set; }

        /// <summary>Rows per page (default 20, capped at 100).</summary>
        public int? PageSize { get; set; }

        public bool IsPaged => Page.HasValue || PageSize.HasValue;

        public int ResolvedPage => Math.Max(Page ?? 1, 1);

        public int ResolvedPageSize => Math.Clamp(PageSize ?? DefaultPageSize, 1, MaxPageSize);
    }
}
