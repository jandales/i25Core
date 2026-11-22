using i25Core.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace i25Core.BusinessLayer.Implementation.Interface
{
    public interface IRepository
    {
        I25DbContext Context { get; }

        DbSet<T> GetQuery<T>() where T : class;
    }
}