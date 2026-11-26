using i25Core.BusinessLayer.Parameters;
using i25Core.Core.Entities;

namespace i25Core.BusinessLayer.Implementation
{
    public interface IBusinessLayer<T> where T : class
    {
        Task SaveAsync(T entity, CancellationToken ct = default);
        Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
        Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);
        Task DeleteAsync(T entity, CancellationToken ct = default);
        Task AddAsync(T entity, CancellationToken ct = default);
        Task UpdateSync(T entity, CancellationToken ct = default);
        Task<GetResponse> GetAsync(GetRequest request, CancellationToken ct = default);
    }
}
