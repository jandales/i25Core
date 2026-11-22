using i25Core.BusinessLayer.Parameters;
using i25Core.Core.Entities;

namespace i25Core.BusinessLayer.Implementation
{
    public interface IBusinessLayer<T> where T : class
    {
        Task SaveAsync(T entity);
        Task<T?> GetByIdAsync(int id);
        Task<IEnumerable<T>> GetAllAsync();
        Task DeleteAsync(T entity);
        Task AddAsync(T entity);
        Task UpdateSync(T entity);
        Task<GetResponse> GetAsync(GetRequest request);
    }
}
