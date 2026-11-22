using i25Core.Infrastructure;
using Microsoft.EntityFrameworkCore;
using i25Core.BusinessLayer.Implementation.Interface;

namespace i25Core.BusinessLayer.Implementation
{
    public class BaseRepository : IRepository
    {
        private readonly I25DbContext _context;

        public I25DbContext Context => _context;

        public BaseRepository(I25DbContext context)
        {
            _context = context;
        }

        public DbSet<T> GetQuery<T>() where T : class
        {
            return _context.Set<T>();
        }
    }
}