using i25Core.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using i25Core.BusinessLayer.Implementation.Interface;
using i25Core.BusinessLayer.Extensions;
using i25Core.BusinessLayer.Parameters;
using i25Core.Core.Exceptions;

namespace i25Core.BusinessLayer.Implementation.Base
{
    public class BaseBusinessLayer<T> : IBusinessLayer<T> where T : class
    {
        protected readonly I25DbContext _context;
        protected readonly DbSet<T> _dbSet;

        public BaseBusinessLayer(IRepository db)
        {
            _context = db.Context;
            _dbSet = db.GetQuery<T>();
        }

        public virtual async Task AddAsync(T entity)
        {
           await _dbSet.AddAsync(entity);
           await SaveAsync(entity);
        }

        public virtual async Task UpdateSync(T entity)
        {
            var existing = await _dbSet.FindAsync(FindPrimaryKey(entity));

            if (existing == null)
            {
                throw new NotFoundException($"{typeof(T).Name} was not found.");
            }

            _context.Entry(existing).CurrentValues.SetValues(entity);

            await SaveAsync(entity);
        }
        public virtual async Task SaveAsync(T entity)
        {   
            await _context.SaveChangesAsync();
        }


        public virtual async Task<T?> GetByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);        
        }       

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {           
            return await _dbSet.ToListAsync();
        }

        public async Task DeleteAsync(T entity)
        {          
            var model = await _dbSet.FindAsync(FindPrimaryKey(entity));
            if (model != null)
            {
                _dbSet.Remove(model);
                await SaveAsync(entity);
            } 
        }  

        public async Task<bool> ExecuteStoredProcedureAsync(string procedureName,  params SqlParameter[] parameters)
        {
            // Errors (including RAISERROR/THROW from the procedure) bubble up to
            // GlobalExceptionHandler, which logs them and maps them to an ApiResponse.
            var parameterNames = string.Join(", ",
                parameters.Select(p => $"@{p.ParameterName.TrimStart('@')}"));

            var sql = $"EXEC {procedureName} {parameterNames}";

            await _context.Database.ExecuteSqlRawAsync(sql, parameters);

            return true;
        }
        private object?[] FindPrimaryKey(T entity)
        {
            var entityType = _context.Model.FindEntityType(typeof(T));

            var primaryKey = entityType?.FindPrimaryKey()
                ?? throw new InvalidOperationException(
                    $"{typeof(T).Name} does not have a primary key.");

            return primaryKey.Properties
                .Select(property =>
                    _context.Entry(entity)
                        .Property(property.Name).CurrentValue)
                .ToArray();
        }

        public virtual async Task<GetResponse> GetAsync(GetRequest request)
        {
            IQueryable<T> query = _dbSet;       

            if (request?.Filters != null && request.Filters.Any())
            {
                query = query.Filter(request.Filters);
            }

            if (request?.Sorts?.Any() == true)
                query = query.Sort(request.Sorts);
            
            var data = await query.ToListAsync();         

            return new GetResponse
            {
                Success = true,
                Message = "Records retrieved successfully.",
                Data = data
            };
        }
    }
}
